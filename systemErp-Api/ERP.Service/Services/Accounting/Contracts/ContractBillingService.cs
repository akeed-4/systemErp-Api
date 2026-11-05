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

    public ContractBillingService(ErpDbContext db, ISalesInvoiceService sales, IPurchaseInvoiceService purchases, IAccountingPostingService posting)
    {
        _db = db; _sales = sales; _purchases = purchases; _posting = posting;
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
            var cert = await _db.Set<ContractCertificate>().FirstOrDefaultAsync(x => x.Id == certificateId && x.ContractId == contractId, token)
                ?? throw new NotFoundException(Messages.CertificateNotFound);
            if (cert.Status != St.Approved) throw new ConflictException(string.Format(Messages.CertificateStatusInvalid, cert.Status));
            if (cert.TaxableAmount <= 0) throw new ValidationFailedException(Messages.CertificateNothingToInvoice);

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
            var advanceAccount = cert.AdvanceRecovery > 0
                ? settings.ContractAccount(supplier ? nameof(AccountingSettings.ContractAdvancesPaidAccountCode) : nameof(AccountingSettings.ContractAdvancesReceivedAccountCode))
                : null;

            var day = InvoiceDay(date ?? cert.CertificateDate);
            var title = $"مستخلص رقم {cert.SequenceNo} ({cert.CertificateNumber}) - عقد {c.ContractNumber}"
                + (cert.AdvanceRecovery > 0 ? $" — أعمال {cert.CurrentWorkValue:0.00} ناقص استرداد دفعة مقدمة {cert.AdvanceRecovery:0.00}" : string.Empty);
            var request = await InvoiceRequestAsync(c, title, cert.TaxableAmount, cert.VatRate, day,
                "contract_certificate", cert.Id, cert.CertificateNumber, token);
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

    // ---------- الإفراج عن الضمان ----------

    public Task<RetentionReleaseResultDto> ReleaseRetentionAsync(Guid contractId, DateTime? date = null, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var c = await LoadBoqContractAsync(contractId, token);
            // الإفراج بعد الاستلام الأولي (والإغلاق يشترطه)
            if (c.Stage != ContractStageDefinition.InitialAcceptance) throw new ConflictException(Messages.RetentionReleaseAfterAcceptance);
            var invoices = _db.Set<ContractInvoice>().Where(i => i.ContractId == c.Id && i.Kind != "credit_note");
            var withheld = await invoices.Where(i => i.Kind != RetentionRelease).SumAsync(i => i.RetentionAmount, token);
            var released = await invoices.Where(i => i.Kind == RetentionRelease).SumAsync(i => i.GrossAmount, token);
            var amount = withheld - released;
            if (amount <= 0) throw new ConflictException(Messages.RetentionNothingToRelease);

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
