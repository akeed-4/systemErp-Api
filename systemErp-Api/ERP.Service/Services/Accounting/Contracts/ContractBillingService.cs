using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.ContractCertificateStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// فوترة عقود جدول الكميات. القيود (عقد عميل ← نحن المقاول؛ عقد مورد ← مقاول باطن نعتمد مستخلصه):
/// <list type="bullet">
/// <item>الدفعة المقدمة: فاتورة بقيمة الدفعة وضريبتها. العميل: إيرادها على «دفعات مقدمة مستلمة» (التزام).
/// المورد: فاتورة مشتريات ثم إعادة تصنيف من الخدمات المشتراة إلى «دفعات مقدمة مدفوعة» (أصل).</item>
/// <item>المستخلص: فاتورة بالخاضع (الحالي − الاسترداد) وضريبته؛ الضمان والغرامة والخصومات الأخرى أجزاء «استقطاع عقد» غير آجلة
/// على حساباتها، فيبقى على ذمة الطرف الصافي المستحق وحده (ويُحسب منه المتبقي والتحصيل). الاسترداد بقيد مرافق:
/// العميل من «الدفعات المستلمة» إلى الإيراد، والمورد من الخدمات المشتراة إلى «الدفعات المدفوعة».</item>
/// <item>الإفراج عن الضمان: قيد بلا فاتورة (الضريبة سبقت في المستخلصات) يعيد المحتجز لذمة الطرف.</item>
/// <item>تكلفة المواد: مع فاتورة المستخلص قيد مدين «تكلفة إيرادات العقود» / دائن «مواد مشاريع تحت التنفيذ» بالتكلفة الفعلية لكميات
/// أذونات التسليم المختارة فيه (لا السعر ولا تقدير جدول الكميات). المخزون خرج عند التسليم فلا يُقيَّد هنا مرة أخرى.</item>
/// <item>إلغاء فاتورة المستخلص: مرتجع كامل (إشعار دائن/مدين) بمنطق المرتجعات نفسه، وعكس قيدي الاسترداد والتكلفة، والمستخلص يصير
/// ملغى فتعود كمياته ومواده متاحة. مرة واحدة (فهرس فريد للإشعار)، وللمستخلص الأخير غير المحصّل فقط.</item>
/// </list>
/// الحسابات من إعدادات المحاسبة بلا افتراضي: غير المحدد يُرفض برسالة الإعداد الناقص. كل عملية في معاملة واحدة.
/// </summary>
public class ContractBillingService : IContractBillingService
{
    private const string Advance = "advance";
    private const string RetentionRelease = "retention_release";

    private readonly ErpDbContext _db;
    private readonly ISalesInvoiceService _sales;
    private readonly IPurchaseInvoiceService _purchases;
    private readonly IAccountingPostingService _posting;
    private readonly IVoucherService _vouchers;
    private readonly IContractCertificateService _certificates;
    private readonly ISalesReturnService _salesReturns;
    private readonly IPurchaseReturnService _purchaseReturns;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public ContractBillingService(ErpDbContext db, ISalesInvoiceService sales, IPurchaseInvoiceService purchases, IAccountingPostingService posting,
        IVoucherService vouchers, IContractCertificateService certificates, ISalesReturnService salesReturns, IPurchaseReturnService purchaseReturns,
        ICurrentUser user, IAuditService audit)
    {
        _db = db; _sales = sales; _purchases = purchases; _posting = posting; _vouchers = vouchers; _certificates = certificates;
        _salesReturns = salesReturns; _purchaseReturns = purchaseReturns; _user = user; _audit = audit;
    }

    // ---------- الدفعة المقدمة ----------

    public Task<InvoiceDto> BillAdvanceAsync(Guid contractId, DateTime? date = null, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var c = await LoadBoqContractAsync(contractId, token);
            RequireActive(c);
            if (c.AdvancePercent <= 0) throw new ValidationFailedException(Messages.ContractAdvanceNotDefined);
            if (await _db.Set<ContractInvoice>().AnyAsync(i => i.ContractId == c.Id && i.Kind == Advance, token))
                throw new ConflictException(Messages.ContractAdvanceAlreadyBilled);
            var amount = DocumentPricing.Round(c.ContractValue * c.AdvancePercent / 100m);
            if (amount <= 0) throw new ValidationFailedException(Messages.ContractAdvanceNotDefined);

            // منشأة قائمة لم تُهيَّأ حسابات عقودها: تُنشأ/تُربط الناقصة فقط (الإعداد المحدد لا يُمس)
            await ContractAccountsSetup.EnsureMissingAsync(_db, token);
            var settings = await PostingAccounts.SettingsAsync(_db, token);
            var supplier = IsSupplier(c);
            // الحسابان يُطلبان قبل أي ترحيل: لا فاتورة بلا الحساب الذي ستُسترد منه الدفعة
            var advanceAccount = settings.ContractAccount(supplier
                ? nameof(AccountingSettings.ContractAdvancesPaidAccountCode) : nameof(AccountingSettings.ContractAdvancesReceivedAccountCode));
            var day = InvoiceDay(date);

            var request = await InvoiceRequestAsync(c, $"دفعة مقدمة {c.AdvancePercent:0.##}% - عقد {c.ContractNumber}", amount, c.VatRate, day,
                "commercial_contract", c.Id, c.ContractNumber, token);
            if (!supplier) request.RevenueAccountCode = advanceAccount; // إيراد الدفعة التزام حتى يُسترد بالمستخلصات
            var invoice = await CreateInvoiceAsync(request, supplier, token);

            Guid? journal = null;
            if (supplier)
            {
                // المشتريات تُرحَّل على الخدمات المشتراة: تُعاد إلى الدفعات المدفوعة (أصل) حتى تُسترد
                var note = $"إعادة تصنيف دفعة مقدمة {invoice.InvoiceNumber} - عقد {c.ContractNumber}";
                journal = (await PostAsync(c, day, note, invoice.InvoiceNumber, "contract_advance", c.Id,
                    new PostingLine(advanceAccount, amount, 0, note, c.CostCenterId),
                    new PostingLine(DefaultAccounts.PurchasedServices, 0, amount, note, c.CostCenterId), token)).JournalEntryId;
            }

            _db.Add(new ContractInvoice
            {
                ContractId = c.Id, Kind = Advance, InvoiceId = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, JournalEntryId = journal,
                GrossAmount = amount, VatAmount = invoice.VatTotal, TotalAmount = invoice.GrandTotal, IssuedAt = invoice.IssueDate,
            });
            AddInvoiced(c, invoice.GrandTotal);
            await _db.SaveChangesAsync(token);
            return invoice;
        }, ct);

    // ---------- المستخلص ----------

    public Task<InvoiceDto> BillCertificateAsync(Guid contractId, Guid certificateId, DateTime? date = null, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var c = await LoadBoqContractAsync(contractId, token);
            RequireActive(c);
            var cert = await _db.Set<ContractCertificate>().Include(x => x.Materials).FirstOrDefaultAsync(x => x.Id == certificateId && x.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.CertificateNotFound);
            if (cert.Status != St.Approved) throw new ConflictException(string.Format(Messages.CertificateStatusInvalid, cert.Status));
            if (cert.TaxableAmount <= 0) throw new ValidationFailedException(Messages.CertificateNothingToInvoice);
            // تكلفة المواد: ما رُحِّل تسليمه على أصل تحت التنفيذ يُنقل لتكلفة الإيرادات من الحساب نفسه (الجرد الدوري بلا أصل فلا قيد)
            var materialCost = await MaterialCostByWipAsync(cert, token);

            // منشأة قائمة لم تُهيَّأ حسابات عقودها: تُنشأ/تُربط الناقصة فقط (الإعداد المحدد لا يُمس)
            await ContractAccountsSetup.EnsureMissingAsync(_db, token);
            var settings = await PostingAccounts.SettingsAsync(_db, token);
            var supplier = IsSupplier(c);
            // الاستقطاعات على حساباتها (المتطابقة تُجمع في جزء واحد)؛ الحساب الناقص يُرفض قبل أي ترحيل
            var settlements = new Dictionary<string, decimal>();
            void Settle(string key, decimal amount)
            {
                if (amount <= 0) return;
                var code = settings.ContractAccount(key);
                settlements[code] = settlements.GetValueOrDefault(code) + amount;
            }
            Settle(supplier ? nameof(AccountingSettings.ContractRetentionPayableAccountCode) : nameof(AccountingSettings.ContractRetentionReceivableAccountCode), cert.RetentionAmount);
            Settle(supplier ? nameof(AccountingSettings.ContractPenaltiesIncomeAccountCode) : nameof(AccountingSettings.ContractPenaltiesExpenseAccountCode), cert.PenaltyAmount);
            Settle(nameof(AccountingSettings.ContractOtherDeductionsAccountCode), cert.OtherDeductions);
            var costAccount = materialCost.Count > 0 ? settings.ContractAccount(nameof(AccountingSettings.ContractCostOfRevenueAccountCode)) : null;
            var advanceAccount = cert.AdvanceRecovery > 0
                ? settings.ContractAccount(supplier ? nameof(AccountingSettings.ContractAdvancesPaidAccountCode) : nameof(AccountingSettings.ContractAdvancesReceivedAccountCode))
                : null;

            var day = InvoiceDay(date ?? cert.CertificateDate);
            var title = $"مستخلص رقم {cert.SequenceNo} ({cert.CertificateNumber}) - عقد {c.ContractNumber}"
                + (cert.AdvanceRecovery > 0 ? $" — أعمال {cert.CurrentWorkValue:0.00} ناقص استرداد دفعة مقدمة {cert.AdvanceRecovery:0.00}" : string.Empty);
            var request = await InvoiceRequestAsync(c, title, cert.TaxableAmount, cert.VatRate, day,
                "contract_certificate", cert.Id, cert.CertificateNumber, token);
            request.SkipCreditLimit = true; // المستخلص يحكمه العقد لا الحد الائتماني للعميل
            if (!supplier && !string.IsNullOrWhiteSpace(c.RevenueAccountCode)) request.RevenueAccountCode = c.RevenueAccountCode;
            if (settlements.Count > 0)
            {
                // الإجمالي = الخاضع + ضريبته كما يحسبها المحرك؛ الآجل = الإجمالي − الاستقطاعات (= الصافي المستحق)
                var total = cert.TaxableAmount + DocumentPricing.Round(cert.TaxableAmount * cert.VatRate / 100m);
                var credit = total - settlements.Values.Sum();
                request.IsSplitPayment = true;
                request.PaymentSplits = settlements.Select(s => new InvoicePaymentSplitDto
                {
                    Method = PaymentMethod.ContractSettlement, PaymentMethodCode = s.Key, Amount = s.Value, Reference = cert.CertificateNumber,
                }).ToList();
                if (credit > 0) request.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = PaymentMethod.Credit, Amount = credit });
            }
            var invoice = await CreateInvoiceAsync(request, supplier, token);

            Guid? journal = null;
            if (advanceAccount != null)
            {
                var note = $"استرداد دفعة مقدمة - {cert.CertificateNumber} - عقد {c.ContractNumber}";
                var other = supplier ? DefaultAccounts.PurchasedServices
                    : string.IsNullOrWhiteSpace(c.RevenueAccountCode) ? settings.SalesRevenue() : c.RevenueAccountCode;
                // العميل: من الدفعات المستلمة إلى الإيراد؛ المورد: من الدفعات المدفوعة إلى الخدمات المشتراة
                var lines = supplier
                    ? new[] { new PostingLine(other, cert.AdvanceRecovery, 0, note, c.CostCenterId), new PostingLine(advanceAccount, 0, cert.AdvanceRecovery, note, c.CostCenterId) }
                    : new[] { new PostingLine(advanceAccount, cert.AdvanceRecovery, 0, note, c.CostCenterId), new PostingLine(other, 0, cert.AdvanceRecovery, note, c.CostCenterId) };
                journal = (await PostAsync(c, day, note, cert.CertificateNumber, "contract_certificate", cert.Id, lines[0], lines[1], token)).JournalEntryId;
            }

            if (costAccount != null)
            {
                var note = $"تكلفة مواد مستخلص {cert.CertificateNumber} - فاتورة {invoice.InvoiceNumber} - عقد {c.ContractNumber}";
                var total = materialCost.Values.Sum();
                var lines = new List<PostingLine> { new(costAccount, total, 0, note, c.CostCenterId) };
                lines.AddRange(materialCost.Select(w => new PostingLine(w.Key, 0, w.Value, note, c.CostCenterId)));
                cert.CostJournalEntryId = (await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = day, Description = note, SourceType = "contract_certificate_cost", SourceId = cert.Id, SourceNumber = invoice.InvoiceNumber,
                    WarehouseId = c.WarehouseId, Lines = lines,
                }, token)).JournalEntryId;
            }

            cert.Status = St.Invoiced;
            cert.InvoiceId = invoice.Id; cert.InvoiceNumber = invoice.InvoiceNumber; cert.InvoicedAt = DateTime.UtcNow;
            cert.AdvanceJournalEntryId = journal;
            _db.Add(new ContractInvoice
            {
                ContractId = c.Id, Kind = cert.Kind == ContractCertificateKinds.Final ? "final" : "progress",
                InvoiceId = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, CertificateId = cert.Id, JournalEntryId = journal,
                GrossAmount = cert.CurrentWorkValue, AdvanceDeduction = cert.AdvanceRecovery, RetentionAmount = cert.RetentionAmount,
                VatAmount = invoice.VatTotal, TotalAmount = invoice.GrandTotal, IssuedAt = invoice.IssueDate,
            });
            AddInvoiced(c, invoice.GrandTotal);
            await _db.SaveChangesAsync(token);
            return invoice;
        }, ct);

    /// <summary>
    /// العقد المبسط: المستخلص يُفوتر مباشرة بعد إعداده — تقديم واعتماد وفوترة في معاملة واحدة (كلها أو لا شيء)،
    /// بالقواعد نفسها (إعادة القياس، تجاوز الكميات، الحسابات). العقد الكامل يبقى بدورة الاعتماد المنفصلة.
    /// </summary>
    public Task<InvoiceDto> IssueCertificateAsync(Guid contractId, Guid certificateId, DateTime? date = null, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var simplified = await _db.Set<CommercialContract>().AsNoTracking().Where(x => x.Id == contractId).Select(x => (bool?)x.IsSimplified)
                .FirstOrDefaultAsync(token) ?? throw new NotFoundException(Messages.ContractNotFound);
            if (!simplified) throw new ConflictException(Messages.CertificateDirectBillingSimplifiedOnly);
            var status = await _db.Set<ContractCertificate>().AsNoTracking().Where(x => x.Id == certificateId && x.ContractId == contractId)
                .Select(x => x.Status).FirstOrDefaultAsync(token) ?? throw new NotFoundException(Messages.CertificateNotFound);
            if (status == St.Draft) await _certificates.SubmitAsync(contractId, certificateId, token);
            if (status is St.Draft or St.Submitted) await _certificates.ApproveAsync(contractId, certificateId, token);
            return await BillCertificateAsync(contractId, certificateId, date, token);
        }, ct);

    /// <summary>
    /// تكلفة المواد المختارة في المستخلص مجمّعة بحساب «تحت التنفيذ» الذي حُمِّل عليه تسليمها. يتحقق أن أذوناتها ما زالت مرحّلة
    /// (إلغاء الإذن ممنوع ما دامت كمياته مختارة، وهذا حارس أخير). الإذن بلا أصل (جرد دوري/قديم مصروف) لا يولّد قيداً: تكلفته سبقت.
    /// </summary>
    private async Task<Dictionary<string, decimal>> MaterialCostByWipAsync(ContractCertificate cert, CancellationToken ct)
    {
        if (cert.Materials.Count == 0) return new();
        var issueIds = cert.Materials.Select(m => m.IssueId).Distinct().ToList();
        var issues = await _db.Set<ProjectMaterialIssue>().AsNoTracking().Where(i => issueIds.Contains(i.Id))
            .Select(i => new { i.Id, i.IssueNumber, i.Status, i.DebitAccountCode }).ToDictionaryAsync(i => i.Id, ct);
        var stale = issueIds.Where(id => !issues.TryGetValue(id, out var i) || i.Status != ProjectMaterialIssueStatuses.Posted).ToList();
        if (stale.Count > 0)
            throw new ConflictException($"أذونات تسليم في المستخلص لم تعد مرحّلة ({string.Join("، ", stale.Select(id => issues.GetValueOrDefault(id)?.IssueNumber ?? "?"))}): أعد المستخلص وحدّث مواده.");
        return cert.Materials.Where(m => issues[m.IssueId].DebitAccountCode != null)
            .GroupBy(m => issues[m.IssueId].DebitAccountCode!)
            .Select(g => (Account: g.Key, Cost: DocumentPricing.Round(g.Sum(m => m.TotalCost))))
            .Where(x => x.Cost != 0)
            .ToDictionary(x => x.Account, x => x.Cost);
    }

    // ---------- إلغاء فاتورة المستخلص ----------

    /// <summary>
    /// يلغي فاتورة مستخلص مفوتر: مرتجع كامل بمنطق المرتجعات (يعكس الإيراد/الخدمات والضريبة والاستقطاعات كما رُحِّلت)، ثم يعكس قيد
    /// استرداد الدفعة وقيد تكلفة المواد، ويصير المستخلص ملغى فتعود كمياته ومواده متاحة لمستخلص جديد. يُسجَّل إشعار الإلغاء في سجل
    /// فواتير العقد (فهرس فريد يمنع الإلغاء المزدوج). شروطه: المستخلص الأخير غير الملغى، ولا تحصيل على فاتورته، ولا إفراج عن الضمان،
    /// والختامي لا يُلغى بعد الاستلام الأولي. القيود الأصلية تبقى في السجل وتُعكس بقيود مؤرخة، فلا يُعاد كتابة التاريخ.
    /// </summary>
    public Task<ContractCertificateDto> CancelCertificateInvoiceAsync(Guid contractId, Guid certificateId, ContractCertificateActionDto request, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var reason = string.IsNullOrWhiteSpace(request.Note) ? throw new ValidationFailedException(Messages.CertificateNoteRequired) : request.Note.Trim();
            var c = await LoadBoqContractAsync(contractId, token);
            RequireActive(c);
            var cert = await _db.Set<ContractCertificate>().FirstOrDefaultAsync(x => x.Id == certificateId && x.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.CertificateNotFound);
            if (cert.Status != St.Invoiced || cert.InvoiceId is not { } invoiceId)
                throw new ConflictException(string.Format(Messages.CertificateStatusInvalid, cert.Status));
            if (cert.Kind == ContractCertificateKinds.Final && c.Stage != ContractStageDefinition.Active)
                throw new ConflictException("المستخلص الختامي لا تُلغى فاتورته بعد الاستلام الأولي.");
            var later = await _db.Set<ContractCertificate>().AsNoTracking()
                .Where(x => x.ContractId == contractId && x.SequenceNo > cert.SequenceNo && x.Status != St.Cancelled)
                .Select(x => x.CertificateNumber).ToListAsync(token);
            if (later.Count > 0)
                throw new ConflictException($"بُني على هذا المستخلص ما بعده ({string.Join("، ", later)}): ألغِ المستخلصات اللاحقة أولاً.");
            if (await _db.Set<VoucherAllocation>().AnyAsync(a => a.InvoiceId == invoiceId, token))
                throw new ConflictException("على فاتورة المستخلص تحصيل/سداد: ألغِ السند أولاً ثم ألغِ الفاتورة.");
            if (await _db.Set<ContractInvoice>().AnyAsync(i => i.ContractId == contractId && i.Kind == RetentionRelease, token))
                throw new ConflictException("أُفرج عن ضمان العقد: لا تُلغى فواتير مستخلصاته.");
            var billed = await _db.Set<ContractInvoice>().AsNoTracking()
                .FirstOrDefaultAsync(i => i.CertificateId == cert.Id && i.Kind != "credit_note", token);

            var supplier = IsSupplier(c);
            var text = $"إلغاء فاتورة مستخلص {cert.CertificateNumber} ({cert.InvoiceNumber}) - عقد {c.ContractNumber}: {reason}";
            var returnRequest = new CreateReturnInvoiceRequestDto
            {
                OriginalInvoiceId = invoiceId, ReturnReason = text, SystemGenerated = true, ReturnDate = SaudiTime.Today,
            };
            // مرتجع كامل بتوزيع أصله: الآجل يعود على الذمة، والاستقطاعات تعود من حساباتها
            var credit = supplier ? await _purchaseReturns.CreateFromInvoiceAsync(returnRequest, token) : await _salesReturns.CreateFromInvoiceAsync(returnRequest, token);

            if (cert.AdvanceJournalEntryId is { } advance) await _posting.ReverseAsync(advance, text, token);
            if (cert.CostJournalEntryId is { } cost) await _posting.ReverseAsync(cost, text, token);

            _db.Add(new ContractInvoice
            {
                ContractId = c.Id, Kind = "credit_note", InvoiceId = credit.Id, InvoiceNumber = credit.InvoiceNumber, CertificateId = cert.Id,
                RelatedInvoiceId = invoiceId, GrossAmount = billed?.GrossAmount ?? cert.CurrentWorkValue,
                AdvanceDeduction = billed?.AdvanceDeduction ?? cert.AdvanceRecovery, RetentionAmount = billed?.RetentionAmount ?? cert.RetentionAmount,
                VatAmount = credit.VatTotal, TotalAmount = credit.GrandTotal, IssuedAt = credit.IssueDate,
            });
            AddInvoiced(c, -credit.GrandTotal);
            cert.Status = St.Cancelled;
            cert.ReturnNote = reason;
            cert.CreditNoteInvoiceId = credit.Id; cert.CreditNoteNumber = credit.InvoiceNumber;
            cert.InvoiceCancelledAt = DateTime.UtcNow; cert.InvoiceCancelledBy = _user.Name ?? "system";
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("CONTRACT_CERTIFICATE_INVOICE_CANCELLED", nameof(ContractCertificate), cert.Id.ToString(),
                $"{text} — إشعار {credit.InvoiceNumber} بإجمالي {credit.GrandTotal:0.00}، وعُكس قيد التكلفة {cert.MaterialCost:0.00}", token);
            return await _certificates.GetAsync(contractId, cert.Id, token);
        }, ct);

    // ---------- الإفراج عن الضمان ----------

    public Task<RetentionReleaseResultDto> ReleaseRetentionAsync(Guid contractId, DateTime? date = null, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var c = await LoadBoqContractAsync(contractId, token);
            // الإفراج بعد الاستلام الأولي (والإغلاق يشترطه)
            if (c.Stage != ContractStageDefinition.InitialAcceptance) throw new ConflictException(Messages.RetentionReleaseAfterAcceptance);
            var invoices = _db.Set<ContractInvoice>().Where(i => i.ContractId == c.Id && i.Kind != "credit_note");
            // ضمان فواتير مستخلصات أُلغيت بإشعار لم يعد محتجزاً
            var cancelledRetention = await _db.Set<ContractInvoice>().Where(i => i.ContractId == c.Id && i.Kind == "credit_note").SumAsync(i => i.RetentionAmount, token);
            var withheld = await invoices.Where(i => i.Kind != RetentionRelease).SumAsync(i => i.RetentionAmount, token) - cancelledRetention;
            var released = await invoices.Where(i => i.Kind == RetentionRelease).SumAsync(i => i.GrossAmount, token);
            var amount = withheld - released;
            if (amount <= 0) throw new ConflictException(Messages.RetentionNothingToRelease);

            // منشأة قائمة لم تُهيَّأ حسابات عقودها: تُنشأ/تُربط الناقصة فقط (الإعداد المحدد لا يُمس)
            await ContractAccountsSetup.EnsureMissingAsync(_db, token);
            var settings = await PostingAccounts.SettingsAsync(_db, token);
            var supplier = IsSupplier(c);
            var retentionAccount = settings.ContractAccount(supplier
                ? nameof(AccountingSettings.ContractRetentionPayableAccountCode) : nameof(AccountingSettings.ContractRetentionReceivableAccountCode));
            var partyAccount = await PartyAccountAsync(c, supplier, token);
            var day = InvoiceDay(date);
            var note = $"الإفراج عن ضمان حسن التنفيذ - عقد {c.ContractNumber}";
            // العميل: الضمان يعود ذمة عليه؛ المورد: يعود ذمة له
            var posted = supplier
                ? await PostAsync(c, day, note, c.ContractNumber, "contract_retention_release", c.Id,
                    new PostingLine(retentionAccount, amount, 0, note, c.CostCenterId), new PostingLine(partyAccount, 0, amount, note), token)
                : await PostAsync(c, day, note, c.ContractNumber, "contract_retention_release", c.Id,
                    new PostingLine(partyAccount, amount, 0, note), new PostingLine(retentionAccount, 0, amount, note, c.CostCenterId), token);

            _db.Add(new ContractInvoice
            {
                ContractId = c.Id, Kind = RetentionRelease, InvoiceNumber = posted.EntryNumber, JournalEntryId = posted.JournalEntryId,
                GrossAmount = amount, TotalAmount = amount, IssuedAt = day,
            });
            c.RetentionReleaseDate = day;
            await _db.SaveChangesAsync(token);
            return new RetentionReleaseResultDto { JournalEntryId = posted.JournalEntryId, EntryNumber = posted.EntryNumber, Amount = amount };
        }, ct);

    // ---------- التحصيل والسداد ----------

    /// <summary>
    /// تحصيل/سداد فاتورة من فواتير العقد بسند موزَّع عليها: عقد العميل سند قبض على ذمته، وعقد المورد سند صرف لذمته.
    /// الفاتورة يجب أن تكون من فواتير هذا العقد (مستخلص أو دفعة مقدمة)؛ لا يتعلق بمسار العقد (مبسط/كامل).
    /// تحقق المبلغ مقابل المتبقي وطريقة الدفع والترحيل في خدمة السندات نفسها (مصدر الحقيقة للتحصيل).
    /// </summary>
    public async Task<VoucherDto> RecordPaymentAsync(Guid contractId, ContractPaymentRequestDto r, CancellationToken ct = default)
    {
        var c = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        var supplier = IsSupplier(c);
        if (c.PartyId == null)
            throw new ValidationFailedException(supplier ? Messages.LinkContractToSupplierBeforeBilling : Messages.LinkContractToCustomerBeforeBilling);
        if (r.Amount <= 0) throw new ValidationFailedException(Messages.VoucherAmountMustBePositive);

        var billed = await _db.Set<ContractInvoice>().AsNoTracking()
            .Where(i => i.ContractId == c.Id && i.InvoiceId == r.InvoiceId && (i.Kind == Advance || i.Kind == "progress" || i.Kind == "final"))
            .Select(i => new { i.Kind, i.InvoiceNumber, i.CertificateId }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.ContractPaymentInvoiceNotOnContract);

        var partyAccount = await PartyAccountAsync(c, supplier, ct);
        var what = billed.Kind == Advance ? "دفعة مقدمة" : "مستخلص";
        return await _vouchers.CreateAsync(new CreateVoucherDto
        {
            SystemGenerated = true,
            Type = supplier ? VoucherType.Payment : VoucherType.Receipt,
            Date = (r.Date ?? SaudiTime.Today).Date,
            Amount = r.Amount,
            PartyName = c.PartyName,
            PartyAccountCode = partyAccount,
            PaymentMethodCode = string.IsNullOrWhiteSpace(r.PaymentMethodCode) ? null : r.PaymentMethodCode.Trim(),
            TreasuryAccountCode = r.TreasuryAccountCode?.Trim() ?? string.Empty,
            ReferenceNumber = string.IsNullOrWhiteSpace(r.ReferenceNumber) ? billed.InvoiceNumber : r.ReferenceNumber.Trim(),
            Notes = string.IsNullOrWhiteSpace(r.Notes)
                ? $"{(supplier ? "سداد" : "تحصيل")} {what} {billed.InvoiceNumber} - عقد {c.ContractNumber}"
                : r.Notes.Trim(),
            CostCenterId = c.CostCenterId,
            Allocations = new List<VoucherAllocationDto> { new() { InvoiceId = r.InvoiceId, Amount = r.Amount } },
        }, ct);
    }

    // ---------- مساعدات ----------

    /// <summary>طلب فاتورة نظام (رقم تلقائي، مرحّلة، آجلة) بسطر خدمة واحد على العقد ومركز تكلفته؛ المورد من بطاقته بعد التحقق.</summary>
    private async Task<CreateInvoiceDto> InvoiceRequestAsync(CommercialContract c, string title, decimal net, decimal vatRate, DateTime day,
        string referenceType, Guid referenceId, string referenceNumber, CancellationToken ct)
    {
        var request = new CreateInvoiceDto
        {
            SystemGenerated = true,
            Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(c.PartyVatNumber), IssueDate = day,
            PartyId = c.PartyId, PartyName = c.PartyName, PartyVatNumber = c.PartyVatNumber, PartyPhone = c.PartyPhone, PartyEmail = c.PartyEmail,
            PaymentMethod = PaymentMethod.Credit, Status = "posted",
            Notes = title,
            ReferenceType = referenceType, ReferenceId = referenceId, ReferenceNumber = referenceNumber,
            CostCenterId = c.CostCenterId, CostCenterFromSource = true,
            Items = new List<InvoiceItemDto> { new() { ItemId = Guid.Empty, ItemName = title, Unit = "خدمة", Quantity = 1, UnitPrice = net, VatRate = vatRate } },
        };
        if (IsSupplier(c))
        {
            var s = await CommercialContractService.RequireBillableSupplierAsync(_db, c.PartyId!.Value, ct);
            request.Kind = InvoiceKind.Purchase;
            request.InvoiceType = TradeHelper.InvoiceTypeFor(s.VatNumber);
            request.PartyName = s.NameAr; request.PartyVatNumber = s.VatNumber; request.PartyPhone = s.Phone; request.PartyEmail = s.Email;
            request.SendToZatca = false; // فاتورة مقاول الباطن: قيد داخلي فقط
        }
        return request;
    }

    private Task<InvoiceDto> CreateInvoiceAsync(CreateInvoiceDto request, bool supplier, CancellationToken ct)
        => supplier ? _purchases.CreateAsync(request, ct) : _sales.CreateAsync(request, ct);

    private Task<PostingResult> PostAsync(CommercialContract c, DateTime day, string description, string sourceNumber, string sourceType, Guid sourceId,
        PostingLine debit, PostingLine credit, CancellationToken ct)
        => _posting.PostAsync(new GenericPostingRequest
        {
            Date = day, Description = description, SourceType = sourceType, SourceId = sourceId, SourceNumber = sourceNumber,
            WarehouseId = c.WarehouseId, Lines = new List<PostingLine> { debit, credit },
        }, ct);

    /// <summary>حساب ذمة الطرف (العميل أو المورد) من بطاقته.</summary>
    private async Task<string> PartyAccountAsync(CommercialContract c, bool supplier, CancellationToken ct)
    {
        var code = supplier
            ? await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == c.PartyId).Select(s => s.AccountCode).FirstOrDefaultAsync(ct)
            : await _db.Set<Customer>().AsNoTracking().Where(x => x.Id == c.PartyId).Select(x => x.AccountCode).FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(code)
            ? throw new ValidationFailedException(supplier ? Messages.SupplierAccountMissing : Messages.CustomerAccountMissing)
            : code;
    }

    /// <summary>عقد جدول كميات مرتبط بطرف (حساب الذمم من بطاقته).</summary>
    private async Task<CommercialContract> LoadBoqContractAsync(Guid contractId, CancellationToken ct)
    {
        var c = await _db.Set<CommercialContract>().FirstOrDefaultAsync(x => x.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        if (!await CommercialContractService.IsBoqContractAsync(_db, c.ContractType, ct)) throw new ConflictException(Messages.BoqCertificatesOnlyForBoq);
        if (c.PartyId == null)
            throw new ValidationFailedException(IsSupplier(c) ? Messages.LinkContractToSupplierBeforeBilling : Messages.LinkContractToCustomerBeforeBilling);
        return c;
    }

    private static void RequireActive(CommercialContract c)
    {
        if (c.Stage is not (ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance))
            throw new ConflictException(Messages.MilestonesBilledOnlyWhenActive);
    }

    private static bool IsSupplier(CommercialContract c) => string.Equals(c.PartyType, "supplier", StringComparison.OrdinalIgnoreCase);

    private static DateTime InvoiceDay(DateTime? date) => date?.Date ?? SaudiTime.Today;

    private static void AddInvoiced(CommercialContract c, decimal total)
    {
        c.TotalInvoiced += total;
        c.RemainingBalance = c.TotalValueWithVat - c.TotalInvoiced;
    }
}
