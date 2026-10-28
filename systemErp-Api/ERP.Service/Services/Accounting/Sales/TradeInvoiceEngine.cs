using System.Linq.Expressions;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// محرك الفواتير المشترك: البناء والتحقق والتسعير والترحيل (مخزون + قيد + QR) والمرتجعات والقيد، لكل جداول الفواتير المستقلة.
/// <typeparamref name="T"/> في كل عملية هو جدول المستند: SalesInvoice / PurchaseInvoice / SalesReturn / PurchaseReturn
/// للتجارة العامة، أو Invoice لفواتير المعارض (بامتداداتها IInvoiceExtension). لا يُكتب مستند في غير جدوله:
/// فاتورة المعارض (أسطر سيارات، مستند معرض مرجعي، أو مرتجع لأصل معرض) لا تُقبل في جداول التجارة، والعكس.
/// </summary>
public class TradeInvoiceEngine
{
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;
    private readonly IZatcaService _zatca;
    private readonly ITransactionRunner _tx;
    private readonly ERP.Core.Contracts.Shared.IAuditService _audit;
    /// <summary>امتدادات الوحدات (المعارض) على دورة فواتيرها: تعمل على جدول Invoice وحده، ومنطقها خارج المحرك.</summary>
    private readonly IReadOnlyList<IInvoiceExtension> _extensions;
    private readonly IApprovalService _approvals;
    private readonly ERP.Core.Contracts.Shared.IPermissionService _permissions;
    /// <summary>تجاوزات التصنيف الضريبي التي أقرّها آخر بناء؛ تُسجَّل في سجل التدقيق بعد حفظ المستند.</summary>
    private List<ItemTaxTreatment.Override> _taxOverrides = new();

    public TradeInvoiceEngine(ErpDbContext db, IAccountingPostingService posting, IInventoryService inventory,
        INumberSequenceService numbers, IZatcaService zatca, ITransactionRunner tx, ERP.Core.Contracts.Shared.IAuditService audit,
        IEnumerable<IInvoiceExtension> extensions, IApprovalService approvals, ERP.Core.Contracts.Shared.IPermissionService permissions)
    {
        _audit = audit; _extensions = extensions.ToList(); _approvals = approvals; _permissions = permissions;
        _db = db; _posting = posting; _inventory = inventory; _numbers = numbers; _zatca = zatca; _tx = tx;
    }

    /// <summary>نوع المستند الثابت لجدول تجارة عامة؛ null لجدول المعارض (كل الأنواع).</summary>
    public static InvoiceKind? KindOf<T>() where T : InvoiceBase
        => typeof(T) == typeof(SalesInvoice) ? InvoiceKind.Sales
            : typeof(T) == typeof(PurchaseInvoice) ? InvoiceKind.Purchase
            : typeof(T) == typeof(SalesReturn) ? InvoiceKind.SalesReturn
            : typeof(T) == typeof(PurchaseReturn) ? InvoiceKind.PurchaseReturn
            : null;

    private static bool IsShowroom<T>() => typeof(T) == typeof(Invoice);

    /// <summary>الرأس ببنوده وتقسيمات دفعه (وأسطر السيارات لفواتير المعارض).</summary>
    private static IQueryable<T> Detailed<T>(IQueryable<T> q) where T : InvoiceBase
    {
        q = q.Include(nameof(InvoiceBase<InvoiceItem>.Items)).Include(i => i.PaymentSplits);
        if (IsShowroom<T>()) q = q.Include(nameof(Invoice.VehicleLines)).Include(nameof(Invoice.VehicleBatches));
        return q.AsSplitQuery();
    }

    // ---------------- القراءة ----------------
    public async Task<PagedResult<InvoiceDto>> ListAsync<T>(InvoiceKind? kind, PaginationParams p, Expression<Func<T, bool>>? filter, CancellationToken ct)
        where T : InvoiceBase, new()
    {
        var q = _db.Set<T>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (filter != null) q = q.Where(filter);
        if (!string.IsNullOrWhiteSpace(p.Status)) q = q.Where(i => i.Status == p.Status);
        if (p.StartDate.HasValue) q = q.Where(i => i.IssueDate >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(i => i.IssueDate <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(i => i.InvoiceNumber.Contains(t) || i.PartyName.Contains(t) || (i.PartyVatNumber != null && i.PartyVatNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await Detailed(q.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize)).ToListAsync(ct);
        var due = await InvoiceBalances.ForAsync(_db, items.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, ct);
        return new PagedResult<InvoiceDto>
        {
            Items = await MaskAsync(items.Select(i => (i, ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0))).ToList(), ct),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL، والمتبقي يُحسب لفواتير الصفحة فقط.</summary>
    public Task<LoadResult> LoadAsync<T>(InvoiceKind? kind, DataSourceLoadOptions options, Expression<Func<T, bool>>? filter, CancellationToken ct)
        where T : InvoiceBase, new()
    {
        var q = _db.Set<T>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (filter != null) q = q.Where(filter);
        return EntityLoader.LoadAsync(Detailed(q), options, async (page, token) =>
        {
            var due = await InvoiceBalances.ForAsync(_db, page.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, token);
            return await MaskAsync(page.Select(i => (i, ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0))).ToList(), token);
        }, ct, EntityLoader.Desc(nameof(InvoiceBase.IssueDate)), EntityLoader.Desc(nameof(InvoiceBase.InvoiceNumber)));
    }

    public async Task<InvoiceDto> GetAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
    {
        var invoice = await Detailed(_db.Set<T>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException(Messages.InvoiceNotFound);
        var dto = ToDto(invoice, invoice.Status == "posted" ? await InvoiceBalances.DueAsync(_db, id, ct) : 0);
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.FillAsync(car, dto, ct);
        return (await MaskAsync(new List<(T, InvoiceDto)> { (invoice, dto) }, ct))[0];
    }

    private static InvoiceDto ToDto(InvoiceBase invoice, decimal amountDue)
    {
        var dto = Mapper.Map<InvoiceDto>(invoice);
        dto.AmountDue = amountDue;
        return dto;
    }

    /// <summary>حجب ما تحجبه الامتدادات (تكلفة السيارات) عن نتائج فواتير المعارض.</summary>
    private async Task<List<InvoiceDto>> MaskAsync<T>(List<(T Invoice, InvoiceDto Dto)> rows, CancellationToken ct) where T : InvoiceBase
    {
        if (IsShowroom<T>())
        {
            var cars = rows.Select(x => ((Invoice)(object)x.Invoice, x.Dto)).ToList();
            foreach (var ext in _extensions) await ext.MaskAsync(cars, ct);
        }
        return rows.Select(x => x.Dto).ToList();
    }

    // ---------------- الإنشاء ----------------
    public async Task<InvoiceDto> CreateAsync<T>(CreateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await _tx.RunAsync(async token =>
        {
            r.ExistingInvoiceId = null;
            var invoice = await BuildAsync<T>(r, token);
            var draft = string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
            invoice.Status = "draft";
            // المسودة برقم مؤقت؛ الرقم المتسلسل يُصرف عند الترحيل فلا يترك حذف مسودة فجوة في تسلسل الفواتير
            invoice.InvoiceNumber = DraftPrefix + invoice.Id.ToString("N")[..10].ToUpperInvariant();
            _db.Add(invoice);
            await OnSavingAsync(invoice, deleting: false, token);
            await ClaimSourceAsync(invoice, token);
            await _db.SaveChangesAsync(token);
            // سياسة اعتماد منطبقة: تبقى مسودة بطلبها، والترحيل عند الموافقة
            if (!draft && !(r.ApplyApprovalPolicy && await RequiresApprovalAsync(invoice, token))) await FinalizeAsync(invoice, token);
            await LogTaxOverridesAsync(invoice, token); // بعد الترحيل: برقم الفاتورة النهائي
            return await GetAsync<T>(invoice.Id, token);
        }, ct), ct);

    /// <summary>يقيّم سياسات «إنشاء» نوع المستند وينشئ طلب الاعتماد داخل معاملة الإنشاء (يُلغى معها إن فشلت).</summary>
    private async Task<bool> RequiresApprovalAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var documentType = invoice.Kind switch
        {
            InvoiceKind.Sales => "sales",
            InvoiceKind.Purchase => "purchase",
            InvoiceKind.SalesReturn => "sales_return",
            _ => "purchase_return",
        };
        var check = await _approvals.CheckAndCreateAsync(new ApprovalCheckRequestDto
        {
            DocumentType = documentType, ActionType = "create", DocumentId = invoice.Id,
            DocumentNumber = invoice.InvoiceNumber, DocumentAmount = invoice.GrandTotal,
            DocumentDataSnapshot = System.Text.Json.JsonSerializer.Serialize(ToDto(invoice, invoice.GrandTotal)),
        }, ct);
        return check.ApprovalRequired;
    }

    public async Task<InvoiceDto> UpdateAsync<T>(Guid id, UpdateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await _tx.RunAsync(async token =>
        {
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (r.Kind != invoice.Kind) throw new ConflictException(Messages.CannotChangeDocumentType);
            // المرحّلة (قبل إرسالها للهيئة): يُعكس قيدها وحركاتها المخزنية ثم يُعاد ترحيلها بنفس رقمها بعد التعديل
            var wasPosted = invoice.Status == "posted";
            if (wasPosted) await UnpostForEditAsync(invoice, token);
            else
            {
                EnsureNotLinkedToAnotherDocument(invoice);
                if (invoice.Status != "draft") throw new ConflictException(Messages.CannotEditCancelledInvoice);
            }

            r.ExistingInvoiceId = id; // شواصي هذه المسودة المحفوظة ليست تكراراً
            var fresh = await BuildAsync<T>(r, token); // نفس تحقق الإنشاء وحساب الأرقام في السرفر
            // المرفوضة من الهيئة: تُعاد بعد التصحيح كمستند جديد لديها (معرّف UUID جديد، فالهيئة لا تقبل تكراره) وبرقمها نفسه؛
            // ومحاولتها المرفوضة تبقى حلقة في السلسلة ومسجّلة في سجل التدقيق. حالة الهيئة وبصمتها تُصفَّر مع البناء الجديد
            var wasRejected = invoice.ZatcaStatus == ZatcaSubmissionStatus.Rejected;
            var (keepId, keepTenant, keepCreated, keepNumber, keepUuid) = (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, wasRejected ? fresh.Uuid : invoice.Uuid);
            var (keepRefType, keepRefId) = (invoice.ReferenceType, invoice.ReferenceId); // ارتباط المرحّلة بمستندها التجاري يبقى
            var keepEntry = invoice.JournalEntryId; // قيد المرحّلة يُستبدل في مكانه عند إعادة الترحيل

            _db.RemoveRange(invoice.Lines.ToList());
            _db.RemoveRange(invoice.PaymentSplits);
            invoice.ClearLines();
            invoice.PaymentSplits.Clear();
            if (invoice is Invoice car)
            {
                _db.RemoveRange(car.VehicleLines);
                _db.RemoveRange(car.VehicleBatches);
                car.VehicleLines.Clear();
                car.VehicleBatches.Clear();
            }
            Mapper.Apply(fresh, invoice);
            (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid) = (keepId, keepTenant, keepCreated, keepNumber, keepUuid);
            if (wasPosted) (invoice.ReferenceType, invoice.ReferenceId, invoice.JournalEntryId) = (keepRefType, keepRefId, keepEntry);
            invoice.Status = "draft";
            foreach (var item in fresh.Lines.ToList()) invoice.AddLine(item);
            foreach (var split in fresh.PaymentSplits) invoice.PaymentSplits.Add(split);
            if (invoice is Invoice target && fresh is Invoice source)
            {
                foreach (var vl in source.VehicleLines) target.VehicleLines.Add(vl);
                foreach (var batch in source.VehicleBatches)
                {
                    target.VehicleBatches.Add(batch);
                    _db.Add(batch); // معرّفها محدَّد مسبقاً (تشير إليه شواصيها)، فتُضاف صراحةً
                }
            }
            await OnSavingAsync(invoice, deleting: false, token);
            await _db.SaveChangesAsync(token);

            // المرحّلة تعود مرحّلة دائماً (بنفس رقمها) بقيد وحركات مخزنية جديدة
            if (wasPosted || !string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase)) await FinalizeAsync(invoice, token);
            await LogTaxOverridesAsync(invoice, token);
            if (wasPosted)
                await _audit.LogAsync(wasRejected ? "INVOICE_ZATCA_REJECTED_EDITED" : "INVOICE_POSTED_EDITED", typeof(T).Name, invoice.Id.ToString(),
                    $"تعديل {DescribeKind(invoice.Kind)} مرحّلة {invoice.InvoiceNumber}{(wasRejected ? $" بعد رفض الهيئة (معرّف جديد {invoice.Uuid})" : string.Empty)}: عُكس قيدها وحركاتها وأُعيد ترحيلها بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            return await GetAsync<T>(id, token);
        }, ct), ct);

    // امتدادات الوحدات تعمل على فواتيرها (جدول المعارض) وحده
    private async Task OnSavingAsync(InvoiceBase invoice, bool deleting, CancellationToken ct)
    {
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.OnSavingAsync(car, deleting, ct);
    }

    /// <summary>
    /// يلغي أثر ترحيل فاتورة مبيعات/مشتريات تمهيداً لتعديلها وإعادة ترحيلها: يعكس قيدها (بتاريخه، في فترة مفتوحة)
    /// ويعكس حركاتها المخزنية (المبيعات تعود بتكلفة خروجها، والمشتريات تخرج بتكلفتها). يُمنع ما لا يُصحَّح إلا بمرتجع:
    /// المرسلة للهيئة، المرتجعات وما عليه مرتجعات أو سندات موزّعة، فواتير السيارات ومستندات النقاط والعقود،
    /// والمشتريات التي تلتها حركات على أصنافها (يتغيّر بها المتوسط) أو بطريقة الوارد أولاً.
    /// </summary>
    private async Task UnpostForEditAsync(InvoiceBase invoice, CancellationToken ct, bool forDelete = false)
    {
        if (invoice.Kind is not (InvoiceKind.Sales or InvoiceKind.Purchase) || invoice.IsReturn || invoice.OriginalInvoiceId.HasValue)
            throw new ConflictException(Messages.PostedInvoiceLocked);
        // المرسلة للهيئة لا تُعدَّل، إلا المرفوضة: تُصحَّح وتُعاد بمعرّف جديد (الحذف ممنوع لها أيضاً: صارت حلقة في السلسلة)
        if (invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted && (forDelete || invoice.ZatcaStatus != ZatcaSubmissionStatus.Rejected))
            throw new ConflictException(Messages.PostedInvoiceEditZatcaSubmitted);
        // فاتورة نقطة بيع/عقد: مجاميع مستندها المصدر (الوردية، العقد) تتبعها، فتُعدَّل من ذلك المستند
        if (invoice.ReferenceType is not (null or "quotation" or "sales_order" or "purchase_order" or "material_requisition"))
            throw new ConflictException(Messages.InvoiceFromSourceDocument);

        var stockItems = invoice.Lines.Where(i => i.ItemId != Guid.Empty).ToList();
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        // شراء تلته حركات على أصنافه: يُعكس بتكلفته الحالية، وأي فرق عن أثره الأصلي يُرحَّل بقيد إعادة تقييم أدناه
        var restoreAverage = false;
        if (invoice.Kind == InvoiceKind.Purchase && stockItems.Count > 0 && policy.Method == CostingMethod.MovingAverage)
        {
            var ids = stockItems.Select(i => i.ItemId).Distinct().ToList();
            var postedAt = await _db.Set<StockMovement>().AsNoTracking()
                .Where(m => m.SourceId == invoice.Id && m.SourceType == "invoice").MaxAsync(m => (DateTime?)m.CreatedAt, ct);
            restoreAverage = postedAt.HasValue && !await _db.Set<StockMovement>().AsNoTracking()
                .AnyAsync(m => ids.Contains(m.ItemId) && m.CreatedAt > postedAt && (m.SourceId == null || m.SourceId != invoice.Id), ct);
        }

        var reason = string.Format(forDelete ? Messages.PostedInvoiceDeleteReason : Messages.PostedInvoiceEditReason, invoice.InvoiceNumber);
        if (forDelete)
        {
            // الحذف: لا يُحذف مستند بُني عليه مرتجع أو وُزّع عليه سند (يبقى أثرهما بلا أصل)
            if (await _db.Set<InvoiceBase>().AnyAsync(i => i.OriginalInvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedInvoiceDeleteHasReturns);
            if (await _db.Set<VoucherAllocation>().AnyAsync(a => a.InvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedInvoiceDeleteHasPayments);
        }
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.OnUnpostingAsync(car, ct);
        // التعديل: يبقى القيد ويُستبدل في مكانه بنفس رقمه عند إعادة الترحيل (FinalizeAsync). الحذف: يُعكس القيد فيبقى أثره في السجل.
        if (forDelete && invoice.JournalEntryId is { } entryId) await _posting.ReverseAsync(entryId, reason, ct);

        // أثر الفاتورة الأصلي على قيمة المخزون (ما رُحِّل لحساب المخزون): تكلفتها ناقص تكلفة بنود الخدمات
        var serviceCost = invoice.Lines.Where(i => i.ItemId == Guid.Empty && i.UnitCost > 0).Sum(i => Math.Round(i.Quantity * i.UnitCost, 2));
        var originalStockChange = invoice.Kind == InvoiceKind.Purchase ? invoice.TotalCost - serviceCost : -(invoice.TotalCost - serviceCost);
        decimal reversalChange = 0;
        foreach (var item in stockItems)
        {
            var product = await _db.Set<Product>().FirstAsync(p => p.Id == item.ItemId, ct);
            var valueBefore = InventoryValuation.Value(product);
            await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = item.ItemId, WarehouseId = invoice.WarehouseId, Quantity = item.Quantity,
                Type = invoice.Kind == InvoiceKind.Sales ? StockMovementType.AdjustmentIn : StockMovementType.AdjustmentOut,
                UnitCost = invoice.Kind == InvoiceKind.Sales ? item.UnitCost : 0, // المبيعات تعود بتكلفة خروجها
                UnitPrice = item.UnitPrice, ReferenceNumber = invoice.InvoiceNumber, Date = invoice.IssueDate, Notes = reason,
                SourceType = "invoice_reversal", SourceId = invoice.Id,
            }, ct);
            // المتوسط المتحرك: إخراج كمية الشراء بتكلفة شرائها يعيد متوسط ما قبل الشراء (لا شيء بعدها يغيّره)
            if (restoreAverage && product.CurrentStock > 0)
                product.AverageCost = Math.Max(0, Math.Round((valueBefore - item.Quantity * item.UnitCost) / product.CurrentStock, 4));
            reversalChange += InventoryValuation.Value(product) - valueBefore;
        }
        await _db.SaveChangesAsync(ct);
        // هللات التقريب (أو إعادة تسعير آخر شراء): قيد إعادة تقييم يبقي حساب المخزون مساوياً لقيمة الأصناف
        // الفرق تسوية لأثر هذا المستند على حساب مخزون مستودعه؛ المستند بحساب يدوي (السيارات) يبقى على حساب المخزون العام كما كان
        var postingAccounts = await PostingAccounts.SettingsAsync(_db, ct);
        var inventoryAccount = string.IsNullOrWhiteSpace(invoice.InventoryAccountCode)
            ? await postingAccounts.InventoryAsync(_db, invoice.WarehouseId, ct) : postingAccounts.Inventory();
        await InventoryValuation.PostRevaluationAsync(_db, _posting, reversalChange + originalStockChange, inventoryAccount, reason, invoice.Id, ct);
        if (forDelete) invoice.JournalEntryId = null;
    }

    public async Task<InvoiceDto> PostDraftAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await _tx.RunAsync(async token =>
        {
            // حجز المسودة بتحديث مشروط يقفل صفّها حتى نهاية المعاملة: طلب ترحيل متزامن ينتظره ثم يجدها مرحّلة فيُرفض (409)
            // بدل أن يقرأها الطلبان مسودةً فيُرحِّلانها معاً
            var claimed = await _db.Set<T>().Where(i => i.Id == id && i.Status == "draft")
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, DateTime.UtcNow), token);
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (claimed != 1 || invoice.Status != "draft") throw new ConflictException(Messages.InvoiceAlreadyPostedOrCancelled);
            // سير الموافقات: المسودة المعلّقة أو المرفوضة لا تُرحَّل (آخر طلب على المستند هو المرجع)
            var approval = await _db.Set<ApprovalRequest>().Where(r => r.DocumentId == id)
                .OrderByDescending(r => r.CreatedAt).Select(r => r.Status).FirstOrDefaultAsync(token);
            if (approval == "pending") throw new ConflictException(Messages.InvoicePendingApproval);
            if (approval == "rejected") throw new ConflictException(Messages.InvoiceApprovalRejected);
            await FinalizeAsync(invoice, token);
            return await GetAsync<T>(id, token);
        }, ct), ct);

    /// <summary>
    /// الإرسال لهيئة الزكاة بعد اكتمال الترحيل (خارج معاملته): لفاتورة مبيعات مرحّلة طُلب إرسالها من الشاشة أو من
    /// إعداد المنشأة. رفض الهيئة أو تعذّر الاتصال لا يُسقط الفاتورة: تبقى مرحّلة والسبب محفوظ عليها ويُعاد الإرسال.
    /// </summary>
    private async Task<InvoiceDto> SendToZatcaIfRequestedAsync<T>(InvoiceDto invoice, CancellationToken ct) where T : InvoiceBase, new()
    {
        if (!invoice.SendToZatca || invoice.Status != "posted" || invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted) return invoice;
        if (invoice.Kind is not (InvoiceKind.Sales or InvoiceKind.SalesReturn)) return invoice;
        // داخل معاملة مستند آخر (نقطة بيع، عقد، مرتجع): الفاتورة لم تُثبَّت بعد، فلا تُرسل قبل اكتمال معاملته
        if (_db.Database.CurrentTransaction != null) return invoice;
        try
        {
            await _zatca.SubmitInvoiceAsync(invoice.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _audit.LogAsync("ZATCA_SUBMIT_FAILED", typeof(T).Name, invoice.Id.ToString(), $"{invoice.InvoiceNumber}: {ex.Message}", ct);
        }
        return await GetAsync<T>(invoice.Id, ct);
    }

    /// <summary>
    /// إرسال فاتورة أنشأتها وحدة داخل معاملتها (عقد بيع السيارة) بعد اكتمال تلك المعاملة، بقواعد الإرسال التلقائي نفسها:
    /// المطلوب إرسالها ولم تُرسل بعد فقط، والرفض أو تعذّر الاتصال يُحفظ على الفاتورة ولا يُفشل العملية.
    /// </summary>
    public async Task<InvoiceDto> SubmitIfRequestedAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await GetAsync<T>(id, ct), ct);

    /// <summary>
    /// المرتجع يتبع فاتورته في الإرسال للهيئة: طُلب إرسالها أو وصلتها فعلاً. الإشعار الدائن لفاتورة في سلسلة الهيئة يلزم أن يصلها،
    /// وفاتورة لم تُطلب لها لا يُرسل مرتجعها.
    /// </summary>
    public static bool ReturnSendsToZatca(InvoiceBase original)
        => original.SendToZatca || original.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted;

    /// <summary>
    /// مرتجع (إشعار دائن/مدين) لفاتورة مرحّلة من جدول <typeparamref name="TOriginal"/>، يُنشأ في جدول <typeparamref name="TReturn"/>
    /// (مبيعات ← مرتجع مبيعات، مشتريات ← مرتجع مشتريات، والمعارض في جدولها).
    /// </summary>
    public async Task<InvoiceDto> CreateReturnAsync<TOriginal, TReturn>(CreateReturnInvoiceRequestDto r, CancellationToken ct)
        where TOriginal : InvoiceBase, new() where TReturn : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<TReturn>(await _tx.RunAsync(async token =>
        {
            var original = await Detailed(_db.Set<TOriginal>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, token)
                ?? throw new NotFoundException(Messages.OriginalInvoiceNotFound);
            if (original.Status != "posted" || original.IsReturn) throw new ConflictException(Messages.OnlyPostedOriginalInvoiceCanBeReturned);
            if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException(Messages.ReturnReasonRequired);
            if (r.RefundPaymentMethod.HasValue)
                r.RefundPaymentMethod = await PaymentMethodRules.MethodForAsync(_db, r.RefundPaymentMethod.Value, r.RefundPaymentMethodCode, token);
            if (original is Invoice car)
                foreach (var ext in _extensions)
                    if (await ext.CreateReturnAsync(r, car, CreateAsync<TReturn>, token) is { } extensionReturn) return extensionReturn;

            var previouslyReturned = (await _db.Set<InvoiceItemBase>().AsNoTracking()
                    .Where(i => _db.Set<TReturn>().Any(inv => inv.Id == i.InvoiceId && inv.OriginalInvoiceId == original.Id && inv.Status == "posted"))
                    .ToListAsync(token))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var originalLines = original.Lines.ToList();
            var lines = r.Lines.Count > 0
                ? r.Lines
                : originalLines.Select(i => new ReturnLineDto { ItemId = i.ItemId, Quantity = i.Quantity - previouslyReturned.GetValueOrDefault(i.ItemId) }).Where(l => l.Quantity > 0).ToList();
            if (lines.Count == 0) throw new ConflictException(Messages.NoRemainingQtyToReturn);

            var dto = new CreateInvoiceDto
            {
                Kind = original.Kind == InvoiceKind.Sales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn,
                InvoiceType = original.Kind == InvoiceKind.Sales ? InvoiceType.CreditNote : InvoiceType.DebitNote,
                IsReturn = true, OriginalInvoiceId = original.Id, OriginalInvoiceNumber = original.InvoiceNumber, ReturnReason = r.ReturnReason,
                PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber, PartyIdType = original.PartyIdType, PartyIdNumber = original.PartyIdNumber,
                PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
                WarehouseId = original.WarehouseId, // المرتجع يعود إلى مستودع فاتورته
                CostCenterId = original.CostCenterId,
                // الحسابات المحددة يدوياً على الأصل تتقدم في مرتجعه كما تقدمت فيه: المخزون يعود لحسابه والإيراد يُعكس من حسابه
                RevenueAccountCode = original.RevenueAccountCode, InventoryAccountCode = original.InventoryAccountCode, CogsAccountCode = original.CogsAccountCode,
                VatTreatment = original.VatTreatment, VatTreatmentReasonCode = original.VatTreatmentReasonCode, // المرتجع بمعالجة أصله
                PartyStreet = original.PartyStreet, PartyBuildingNo = original.PartyBuildingNo, PartyDistrict = original.PartyDistrict,
                PartyCity = original.PartyCity, PartyPostalCode = original.PartyPostalCode, PartyAdditionalNo = original.PartyAdditionalNo, PartyCountry = original.PartyCountry,
                PaymentMethod = r.RefundPaymentMethod ?? original.PaymentMethod, PaymentMethodCode = r.RefundPaymentMethod == null ? original.PaymentMethodCode : r.RefundPaymentMethodCode, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,
                Notes = ReturnNotes(original, r),
                Status = "posted",
            };
            if (r.ReturnDate.HasValue) dto.IssueDate = r.ReturnDate.Value;
            foreach (var l in lines)
            {
                var src = originalLines.FirstOrDefault(i => i.ItemId == l.ItemId) ?? throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
                var remaining = src.Quantity - previouslyReturned.GetValueOrDefault(l.ItemId);
                if (l.Quantity <= 0 || l.Quantity > remaining)
                    throw new ValidationFailedException(string.Format(Messages.ReturnQtyRange, src.ItemName, remaining));
                dto.Items.Add(new InvoiceItemDto
                {
                    ItemId = src.ItemId, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit, Quantity = l.Quantity,
                    UnitPrice = src.UnitPrice, UnitCost = src.UnitCost, VatRate = src.VatRate,
                    ExciseTaxRate = src.ExciseTaxRate, ExciseRetailPrice = src.ExciseRetailPrice,
                    ExciseStandardPrice = src.ExciseStandardPrice,
                    VatCategory = src.VatCategory, VatExemptionReasonCode = src.VatExemptionReasonCode, VatOverrideReason = src.VatOverrideReason,
                    RevenueAccountCode = src.RevenueAccountCode,
                    Discount = src.Quantity == 0 ? 0 : Math.Round(src.Discount * l.Quantity / src.Quantity, 2),
                    CostCenterId = src.CostCenterId,
                });
            }
            // خصم الفاتورة يُوزَّع نسبياً على الكميات المرتجعة.
            var origGross = originalLines.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
            var retGross = dto.Items.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
            dto.InvoiceDiscount = origGross == 0 ? 0 : DocumentPricing.Round(original.InvoiceDiscount * retGross / origGross);

            if (r.RefundSplits.Count > 0) await ApplyRefundSplitsAsync(_db, dto, r.RefundSplits, token);
            else if (r.RefundPaymentMethod == null) MirrorOriginalSplits(dto, original);
            return await CreateAsync<TReturn>(dto, token);
        }, ct), ct);

    internal static string ReturnNotes(InvoiceBase original, CreateReturnInvoiceRequestDto r)
        => string.IsNullOrWhiteSpace(r.Notes) ? $"مرتجع من الفاتورة {original.InvoiceNumber}" : $"مرتجع من الفاتورة {original.InvoiceNumber}: {r.Notes.Trim()}";

    /// <summary>
    /// رد المرتجع بأكثر من طريقة: المبالغ المدخلة تُوزَّع بنسبتها على إجمالي المرتجع المحسوب في السرفر (والكسر على آخر سطر)،
    /// فلا يفشل الحفظ لفرق تقريب بين تقدير الشاشة والإجمالي الفعلي. كل طريقة تُعتمد (فعّالة) ونوع الدفع من نوعها.
    /// يُستدعى بعد اكتمال أسطر المرتجع وخصمه.
    /// </summary>
    internal static async Task ApplyRefundSplitsAsync(ErpDbContext db, CreateInvoiceDto dto, List<InvoicePaymentSplitDto> splits, CancellationToken ct)
    {
        var lines = splits.Where(s => s.Amount > 0).ToList();
        if (lines.Count == 0) return;
        var total = DocumentPricing.Price(
            dto.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride,
                i.ExciseTaxRate, i.ExciseRetailPrice, i.ExciseStandardPrice)).ToList(), dto.InvoiceDiscount).GrandTotal;
        var entered = lines.Sum(s => s.Amount);
        dto.PaymentSplits.Clear();
        var allocated = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var s = lines[i];
            var amount = i == lines.Count - 1 ? total - allocated : DocumentPricing.Round(s.Amount * total / entered);
            allocated += amount;
            var method = await PaymentMethodRules.MethodForAsync(db, s.Method, s.PaymentMethodCode, ct);
            if (amount > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = method, PaymentMethodCode = s.PaymentMethodCode, Amount = amount, Reference = s.Reference });
        }
        if (dto.PaymentSplits.Count == 0) return;
        dto.IsSplitPayment = true;
        dto.PaymentMethod = dto.PaymentSplits.Any(s => s.Method == PaymentMethod.Credit) ? PaymentMethod.Credit : dto.PaymentSplits[0].Method;
        dto.PaymentMethodCode = null;
    }

    /// <summary>بلا طريقة ردّ محددة: فاتورة دُفعت بأكثر من طريقة يُردّ مرتجعها بالتوزيع نفسه (بنسبة قيمة المرتجع).</summary>
    internal static void MirrorOriginalSplits(CreateInvoiceDto dto, InvoiceBase original)
    {
        if (original.PaymentSplits.Count == 0 || original.GrandTotal <= 0) return;
        var returned = DocumentPricing.Price(
            dto.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride,
                i.ExciseTaxRate, i.ExciseRetailPrice, i.ExciseStandardPrice)).ToList(), dto.InvoiceDiscount).GrandTotal;
        var allocated = 0m;
        for (var i = 0; i < original.PaymentSplits.Count; i++)
        {
            var split = original.PaymentSplits.ElementAt(i);
            var amount = i == original.PaymentSplits.Count - 1
                ? returned - allocated : DocumentPricing.Round(split.Amount * returned / original.GrandTotal);
            allocated += amount;
            if (amount > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = split.Method, PaymentMethodCode = split.PaymentMethodCode, Amount = amount, Reference = split.Reference });
        }
        dto.IsSplitPayment = dto.PaymentSplits.Count > 0;
    }

    /// <summary>حذف مسودة فقط. الفاتورة المرحّلة مستند صادر لا يُحذف: تُلغى بمرتجع كامل (إشعار دائن/مدين).</summary>
    public Task DeleteAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => _tx.RunAsync(async token =>
        {
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            // ما وصل للهيئة لا يُحذف أبداً، مقبولاً أو مرفوضاً: صار حلقة في سلسلة التجزئة والعدّاد، ويُصحَّح بمرتجع فقط
            if (invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted || invoice.ZatcaHash != null)
                throw new ConflictException(string.Format(Messages.InvoiceSentToZatcaCannotBeDeleted, invoice.InvoiceNumber));
            // المرحّلة (قبل إرسالها للهيئة) تُحذف: يُعكس قيدها وحركاتها وتعود سياراتها؛ المسودة تُحذف مباشرة
            var wasPosted = invoice.Status == "posted";
            if (wasPosted) await UnpostForEditAsync(invoice, token, forDelete: true);
            else EnsureNotLinkedToAnotherDocument(invoice);
            await ReleaseSourceAsync(invoice, token);
            await OnSavingAsync(invoice, deleting: true, token);
            await _audit.LogAsync(wasPosted ? "INVOICE_POSTED_DELETED" : "INVOICE_DRAFT_DELETED", typeof(T).Name, invoice.Id.ToString(), $"حذف {(wasPosted ? $"{invoice.InvoiceNumber} المرحّلة" : "مسودة")} {DescribeKind(invoice.Kind)} بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            _db.RemoveRange(invoice.Lines.ToList());
            _db.RemoveRange(invoice.PaymentSplits);
            if (invoice is Invoice car)
            {
                _db.RemoveRange(car.VehicleLines);
                _db.RemoveRange(car.VehicleBatches);
            }
            _db.Remove(invoice);
            await _db.SaveChangesAsync(token);
        }, ct);

    private static void EnsureNotLinkedToAnotherDocument(InvoiceBase invoice)
    {
        if (invoice.ReferenceType != null || invoice.ReferenceId.HasValue || invoice.OriginalInvoiceId.HasValue)
            throw new ConflictException(Messages.InvoiceFromSourceDocument);
    }

    /// <summary>
    /// فاتورة شراء مستوردة من طلب شراء في الشاشة: الطلب يجب أن يكون معتمداً وغير محوَّل، ويُعلَّم محوَّلاً إليها
    /// (عكس <see cref="ReleaseSourceAsync"/>). التحويل المباشر من شاشة الطلبات يمرّ بهذا المسار أيضاً.
    /// </summary>
    private async Task ClaimSourceAsync(InvoiceBase invoice, CancellationToken ct)
    {
        if (invoice.ReferenceType != "material_requisition" || invoice.ReferenceId == null) return;
        if (invoice.Kind != InvoiceKind.Purchase) throw new ValidationFailedException(Messages.InvalidDocumentType);
        var m = await _db.Set<MaterialRequisition>().FirstOrDefaultAsync(x => x.Id == invoice.ReferenceId, ct)
            ?? throw new NotFoundException(Messages.PurchaseRequisitionNotFound);
        if (m.ConvertedInvoiceId != null || m.Status is "converted_to_invoice" or "converted_to_po")
            throw new ConflictException(string.Format(Messages.RequisitionAlreadyConverted, m.RequisitionNumber));
        if (m.Status != "approved") throw new ConflictException(Messages.ApproveRequisitionBeforeConversion);
        m.ConvertedInvoiceId = invoice.Id;
        m.Status = "converted_to_invoice";
    }

    /// <summary>عند حذف فاتورة محوّلة من مستند تجاري يُفَك الارتباط ليعود المستند قابلاً للتحويل.</summary>
    private async Task ReleaseSourceAsync(InvoiceBase invoice, CancellationToken ct)
    {
        if (invoice.ReferenceId == null) return;
        var sourceId = invoice.ReferenceId.Value;
        switch (invoice.ReferenceType)
        {
            case "quotation":
                var q = await _db.Set<Quotation>().FirstOrDefaultAsync(x => x.Id == sourceId && x.ConvertedInvoiceId == invoice.Id, ct);
                if (q != null) { q.ConvertedInvoiceId = null; q.Status = "accepted"; }
                break;
            case "sales_order" or "purchase_order":
                var o = await _db.Set<CommercialOrder>().FirstOrDefaultAsync(x => x.Id == sourceId && x.ConvertedInvoiceId == invoice.Id, ct);
                if (o != null) { o.ConvertedInvoiceId = null; o.Status = "confirmed"; }
                break;
            case "material_requisition":
                var m = await _db.Set<MaterialRequisition>().FirstOrDefaultAsync(x => x.Id == sourceId && x.ConvertedInvoiceId == invoice.Id, ct);
                if (m != null) { m.ConvertedInvoiceId = null; m.Status = "approved"; }
                break;
        }
    }

    public async Task<ZatcaSubmitResultDto> SubmitToZatcaAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase
    {
        if (!await _db.Set<T>().AnyAsync(i => i.Id == id, ct)) throw new NotFoundException(Messages.InvoiceNotFound);
        return await _zatca.SubmitInvoiceAsync(id, ct);
    }

    // ---------------- القيد المحاسبي للفاتورة ----------------
    /// <summary>
    /// يُنفَّذ البناء والترحيل الحقيقيان داخل معاملة تُلغى دائماً، فتطابق المعاينة الترحيل الفعلي حرفياً
    /// (تكلفة المخزون بالمتوسط، تكلفة المركبات، حسابات الدفع والطرف، الحد الائتماني) دون أي أثر محفوظ أو رقم مستهلك.
    /// </summary>
    public async Task<InvoiceJournalDto> PreviewJournalAsync<T>(CreateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
    {
        if (_db.Database.CurrentTransaction != null) throw new InvalidOperationException(Messages.JournalPreviewInsideTransaction);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = await BuildAsync<T>(r, ct);
            await EnsureJournalVisibleAsync(invoice, ct);
            invoice.Status = "draft";
            invoice.InvoiceNumber = "PREVIEW-" + invoice.Id.ToString("N")[..8];
            _db.Add(invoice);
            await _db.SaveChangesAsync(ct);
            await FinalizeAsync(invoice, ct, preview: true);
            var journal = await JournalOfAsync(invoice, ct);
            journal.IsPreview = true;
            journal.EntryNumber = null;
            return journal;
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
        }
    }

    public async Task<InvoiceJournalDto> GetJournalAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
    {
        var invoice = await Detailed(_db.Set<T>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(Messages.InvoiceNotFound);
        await EnsureJournalVisibleAsync(invoice, ct);
        if (invoice.JournalEntryId == null) throw new ConflictException(Messages.InvoiceNotPostedUsePreview);
        return await JournalOfAsync(invoice, ct);
    }

    private async Task EnsureJournalVisibleAsync(InvoiceBase invoice, CancellationToken ct)
    {
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.EnsureJournalVisibleAsync(car, ct);
    }

    private async Task<InvoiceJournalDto> JournalOfAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines)
            .FirstAsync(e => e.Id == invoice.JournalEntryId, ct);
        return new InvoiceJournalDto
        {
            EntryNumber = entry.EntryNumber, Date = entry.Date, Description = entry.Description,
            Lines = entry.Lines.OrderByDescending(l => l.Debit > 0).ThenBy(l => l.AccountCode)
                .Select(l => new InvoiceJournalLineDto { AccountCode = l.AccountCode, AccountName = l.AccountName, Debit = l.Debit, Credit = l.Credit, Notes = l.Notes })
                .ToList(),
            TotalDebit = entry.TotalDebit, TotalCredit = entry.TotalCredit,
            Subtotal = invoice.Subtotal, VatTotal = invoice.VatTotal, GrandTotal = invoice.GrandTotal,
            TotalCost = invoice.TotalCost, GrossProfit = invoice.GrossProfit,
        };
    }

    /// <summary>
    /// التصنيف الضريبي يُعرَّف على الصنف. فاتورة المبيعات: كل سطر صنف يأخذ تصنيفه ونسبته وسببه من تعريفه (أو من لقطته في النسخة
    /// المحفوظة عند التعديل)، ولا يغيّره إلا تجاوز بسبب وصلاحية (<see cref="ItemTaxTreatment"/>).
    /// المشتريات: السطر الذي لم يُحدَّد تصنيفه ويرتبط بصنف صفري أو معفى (بسببه) يرثهما منه، وغيره على نسبة سطره (ضريبة المورد).
    /// </summary>
    private async Task ApplyProductVatDefaultsAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        _taxOverrides = new();
        if (r.Kind == InvoiceKind.Sales && !r.IsReturn)
        {
            var itemIds = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
            if (itemIds.Count == 0) return;
            var products = await _db.Set<Product>().AsNoTracking().Where(p => itemIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var previous = r.ExistingInvoiceId is Guid existingId
                ? await _db.Set<InvoiceItemBase>().AsNoTracking().Where(l => l.InvoiceId == existingId && l.ItemId != Guid.Empty).ToListAsync(ct)
                : new List<InvoiceItemBase>();
            _taxOverrides = await ItemTaxTreatment.ApplyAsync(r.Items, products, previous,
                () => _permissions.HasPermissionAsync(PermissionService.InvoiceTaxTreatmentOverrideScreenId, ScreenAction.Edit, ct));
            return;
        }
        var ids = r.Items.Where(i => i.ItemId != Guid.Empty && i.VatCategory == null).Select(i => i.ItemId).Distinct().ToList();
        if (ids.Count == 0) return;
        var defined = await _db.Set<Product>().AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.VatCategory != VatCategory.Standard && p.VatExemptionReasonCode != null)
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var item in r.Items.Where(i => i.VatCategory == null && defined.ContainsKey(i.ItemId)))
        {
            var p = defined[item.ItemId];
            item.VatCategory = p.VatCategory;
            item.VatRate = 0;
            item.VatAmountOverride = null;
            item.VatExemptionReasonCode ??= p.VatExemptionReasonCode;
        }
    }

    private async Task ApplyProductExciseDefaultsAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        // Returns are based on the original invoice snapshot. Purchases are outside this feature's scope.
        if (r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn) return;
        if (r.Kind != InvoiceKind.Sales)
        {
            foreach (var item in r.Items)
            {
                item.ExciseTaxRate = 0;
                item.ExciseRetailPrice = 0;
                item.ExciseStandardPrice = 0;
            }
            return;
        }

        var ids = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var previous = r.ExistingInvoiceId.HasValue
            ? (await _db.Set<InvoiceItemBase>().AsNoTracking().Where(i => i.InvoiceId == r.ExistingInvoiceId.Value && ids.Contains(i.ItemId))
                .ToListAsync(ct)).GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First())
            : new Dictionary<Guid, InvoiceItemBase>();
        foreach (var item in r.Items)
        {
            if (item.ItemId != Guid.Empty && previous.TryGetValue(item.ItemId, out var oldLine))
            {
                item.ExciseTaxRate = oldLine.ExciseTaxRate;
                item.ExciseRetailPrice = oldLine.ExciseRetailPrice;
                item.ExciseStandardPrice = oldLine.ExciseStandardPrice;
            }
            else if (item.ItemId != Guid.Empty && products.TryGetValue(item.ItemId, out var product))
            {
                item.ExciseTaxRate = product.ExciseTaxRate;
                item.ExciseRetailPrice = product.ExciseRetailPrice;
                item.ExciseStandardPrice = product.ExciseStandardPrice;
            }
            else
            {
                item.ExciseTaxRate = 0;
                item.ExciseRetailPrice = 0;
                item.ExciseStandardPrice = 0;
            }
        }
    }

    /// <summary>كل تجاوز جديد للتصنيف الضريبي: سطر في سجل التدقيق بالقيمة الأصلية والجديدة والسبب.</summary>
    private async Task LogTaxOverridesAsync(InvoiceBase invoice, CancellationToken ct)
    {
        foreach (var o in _taxOverrides)
            await _audit.LogAsync("INVOICE_TAX_TREATMENT_OVERRIDE", invoice.GetType().Name, invoice.Id.ToString(), $"{invoice.InvoiceNumber} — {o.Describe()}", ct);
        _taxOverrides = new();
    }

    /// <summary>
    /// فاتورة معارض: أسطر سيارات، أو مستند مرجعي تملكه وحدة المعارض (عقد بيع، أمر شراء)، أو مرتجع لأصل في جدول المعارض.
    /// تُكتب في جدول Invoice وحده؛ وكل ما عداها تجارة عامة في جدول نوعها.
    /// </summary>
    private async Task<bool> IsShowroomRequestAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.VehicleLines.Count > 0 || r.VehicleBatches.Count > 0) return true;
        if (r.ReferenceId.HasValue)
            foreach (var ext in _extensions)
                if (await ext.OwnsReferenceAsync(r.ReferenceType, r.ReferenceId.Value, ct)) return true;
        return r.OriginalInvoiceId.HasValue && await _db.Set<Invoice>().AnyAsync(i => i.Id == r.OriginalInvoiceId, ct);
    }

    // ---------------- البناء والتحقق (بدون أي أثر جانبي) ----------------
    private async Task<T> BuildAsync<T>(CreateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
    {
        // كل مستند في جدوله: فواتير المعارض في Invoice، والتجارة العامة في جدول نوعها
        var showroom = IsShowroom<T>();
        if (showroom != await IsShowroomRequestAsync(r, ct))
            throw new ValidationFailedException(showroom ? Messages.ShowroomInvoicesOnly : Messages.ShowroomInvoiceNotTradeDocument);
        if (KindOf<T>() is { } kind && r.Kind != kind) throw new ValidationFailedException(Messages.InvalidDocumentType);

        if (r.ItemsDerivedFromVehicles) r.Items.Clear(); // إعادة تنفيذ بعد تعارض تزامن: البنود تُشتق من جديد
        // أسطر الوحدات (سيارات المعارض) تشتق r.Items وتتحقق منها
        IInvoiceExtension? owner = null;
        Func<Invoice, CancellationToken, Task>? attachLines = null;
        if (showroom)
            foreach (var ext in _extensions)
                if ((attachLines = await ext.ResolveAsync(r, ct)) != null) { owner = ext; break; }
        r.ItemsDerivedFromVehicles = attachLines != null;
        await ApplyProductVatDefaultsAsync(r, ct);
        await ApplyProductExciseDefaultsAsync(r, ct);
        await EnsureVatTreatmentAllowedAsync(r, ct);
        var errors = new List<string>();
        ApplyDocumentVatTreatment(r, errors);
        if (!Enum.IsDefined(r.Kind)) errors.Add(Messages.InvalidDocumentType);
        if (!Enum.IsDefined(r.InvoiceType)) errors.Add(Messages.InvalidInvoiceType);
        if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneItemRequired);
        if (r.InvoiceDiscount < 0) errors.Add(Messages.InvoiceDiscountCannotBeNegative);
        if (r.ExchangeRate <= 0) errors.Add(Messages.ExchangeRateMustBePositive);
        // التصنيف الضريبي غير الأساسي لا يحمل ضريبة
        if (r.Items.Any(i => i.VatCategory is VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.OutOfScope && (i.VatRate > 0 || i.VatAmountOverride > 0)))
            errors.Add(Messages.VatCategoryRateMismatch);
        // هامش الربح لسطر سيارة مبيعات في فاتورة معرض وحده: ضريبته مبلغ ثابت (لا نسبة)
        if (r.Items.Any(i => i.VatCategory == VatCategory.MarginScheme
                && (!showroom || r.Kind is not (InvoiceKind.Sales or InvoiceKind.SalesReturn) || i.VatRate > 0 || i.ItemId != Guid.Empty)))
            errors.Add(Messages.MarginSchemeItemNotAllowed);
        ValidateExemptionReasons(r, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var isReturn = r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn;
        if (isReturn && r.OriginalInvoiceId == null) throw new ValidationFailedException(Messages.ReturnNeedsOriginalInvoice);
        if (isSales && r.Kind == InvoiceKind.Sales && r.InvoiceType is InvoiceType.CreditNote or InvoiceType.DebitNote)
            throw new ValidationFailedException(Messages.RegularInvoiceTypeInvalid);
        if (r.Kind == InvoiceKind.Purchase && r.InvoiceType == InvoiceType.TaxInvoice && !r.PartyId.HasValue)
            throw new ValidationFailedException(Messages.TaxPurchaseRequiresSupplier);

        var priced = DocumentPricing.Price(
            r.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride,
                i.ExciseTaxRate, i.ExciseRetailPrice, i.ExciseStandardPrice)).ToList(), r.InvoiceDiscount);

        var productIds = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        if (r.Items.Any(i => i.ItemId == Guid.Empty && string.IsNullOrWhiteSpace(i.ItemName)))
            throw new ValidationFailedException(Messages.ServiceLineNeedsDescription);
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new ValidationFailedException(Messages.ItemsNotFoundInInvoice);
        // المراكز من المنشأة ونشطة قبل حفظ المسودة نفسها (المرتجع يتبع مراكز أصله ولو أُوقفت بعده)
        await CostCenterRules.EnsureUsableAsync(_db, r.Items.Select(i => i.CostCenterId).Append(r.CostCenterId), ct, requireActive: !isReturn);

        var invoice = Mapper.Map<T>(r);
        invoice.Id = Guid.NewGuid();
        invoice.Uuid = Guid.NewGuid();
        invoice.Status = "draft";
        invoice.IsReturn = isReturn;
        invoice.Module = showroom ? owner?.Module ?? PlatformModules.CarShowroom : PlatformModules.Accounting;
        // تاريخ ووقت الإصدار بتوقيت المملكة (كما يُطبعان)، ويُحوَّلان للتوقيت العالمي في رمز QR
        invoice.IssueDate = r.IssueDate == default ? SaudiTime.Now : r.IssueDate;
        invoice.IssueTime = string.IsNullOrWhiteSpace(r.IssueTime) ? SaudiTime.Now.ToString("HH:mm:ss") : r.IssueTime;
        var baseCurrency = await _db.Set<Tenant>().AsNoTracking().Select(t => t.Currency).FirstAsync(ct);
        invoice.CurrencyCode = string.IsNullOrWhiteSpace(r.CurrencyCode) ? baseCurrency : r.CurrencyCode.Trim().ToUpperInvariant();
        if (invoice.CurrencyCode == baseCurrency) invoice.ExchangeRate = 1; // العملة الأساسية لا تُحوَّل
        invoice.Subtotal = priced.NetTotal;
        invoice.ItemsDiscountTotal = priced.ItemsDiscountTotal;
        invoice.InvoiceDiscount = priced.InvoiceDiscount;
        invoice.DiscountTotal = priced.DiscountTotal;
        invoice.ExciseTaxTotal = priced.ExciseTaxTotal;
        invoice.VatTotal = priced.VatTotal;
        invoice.GrandTotal = priced.GrandTotal;
        invoice.ZatcaStatus = ZatcaSubmissionStatus.NotSubmitted;
        // خيار الشاشة «إرسال للهيئة»؛ إن لم يُرسل فبحسب إعداد المنشأة (الإرسال التلقائي عند الحفظ)
        invoice.SendToZatca = isSales && (r.SendToZatca
            ?? await _db.Set<Tenant>().AsNoTracking().Select(t => t.ZatcaConfig.AutoSubmitOnInvoiceSave).FirstAsync(ct));
        invoice.ZatcaQrCode = null; invoice.ZatcaHash = null; invoice.ZatcaUblXml = null; invoice.ZatcaPih = null;
        invoice.JournalEntryId = null;

        // الأسطر: الأرقام من السرفر دائماً، وبيانات الصنف من الكتالوج إن لم تُرسل.
        invoice.ClearLines();
        for (var i = 0; i < r.Items.Count; i++)
        {
            var src = r.Items[i]; var pl = priced.Lines[i];
            products.TryGetValue(src.ItemId, out var p); // بند الخدمة: بلا صنف
            var line = invoice.NewLine();
            line.ItemId = src.ItemId;
            line.ItemName = string.IsNullOrWhiteSpace(src.ItemName) ? p!.NameAr : src.ItemName;
            line.Sku = string.IsNullOrWhiteSpace(src.Sku) ? p?.Sku ?? string.Empty : src.Sku;
            line.Unit = string.IsNullOrWhiteSpace(src.Unit) ? p?.Unit ?? string.Empty : src.Unit;
            line.Quantity = src.Quantity; line.UnitPrice = src.UnitPrice; line.Discount = src.Discount;
            line.UnitCost = src.ItemId == Guid.Empty ? src.UnitCost : 0; // بند الخدمة يحمل تكلفته (مثل تكلفة السيارة) لأن لا مخزون يحسبها
            line.VatAmountOverride = src.VatAmountOverride;
            line.VatCategory = src.VatCategory ?? (src.VatRate > 0 || src.VatAmountOverride > 0 ? VatCategory.Standard : VatCategory.ZeroRated);
            line.VatExemptionReasonCode = VatExemptionReasons.Find(src.VatExemptionReasonCode)?.Code;
            line.VatOverrideReason = isSales && !string.IsNullOrWhiteSpace(src.VatOverrideReason) ? src.VatOverrideReason.Trim() : null;
            line.ExciseTaxRate = pl.ExciseTaxRate; line.ExciseRetailPrice = pl.ExciseRetailPrice;
            line.ExciseStandardPrice = pl.ExciseStandardPrice; line.ExciseTaxBaseAmount = pl.ExciseTaxBaseAmount;
            line.ExciseTaxAmount = pl.ExciseTaxAmount;
            line.VatRate = src.VatRate; line.VatAmount = pl.VatAmount; line.TotalBeforeVat = pl.Net; line.TotalAfterVat = pl.Total;
            // مركز الترويسة افتراضي فقط: يُحفظ المركز الفعلي على السطر فلا يغيّر تعديلُ الافتراضي لاحقاً أثرَ المستند
            line.CostCenterId = src.CostCenterId ?? r.CostCenterId;
            line.RevenueAccountCode = isSales && !string.IsNullOrWhiteSpace(src.RevenueAccountCode) ? src.RevenueAccountCode.Trim() : null;
            invoice.AddLine(line);
        }

        if (attachLines != null) await attachLines((Invoice)(object)invoice, ct);
        // هامش ربح السيارات المستعملة المؤهلة: علّقت الهيئة إلزام الفوترة الإلكترونية (المرحلة الثانية) لها منذ مايو 2023 ولا رمز
        // تصنيف لها في مواصفاتها، فلا تُرسل فاتورتها للهيئة (وتبقى بمتطلبات المرحلة الأولى: رمز QR المبسط)
        if (invoice.Lines.Any(l => l.VatCategory == VatCategory.MarginScheme)) invoice.SendToZatca = false;

        // الطرف
        var vatRegisteredCustomer = false;
        if (r.PartyId.HasValue)
        {
            if (isSales)
            {
                var c = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.CustomerNotFound);
                vatRegisteredCustomer = c.IsVatRegistered;
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = c.NameAr;
                invoice.PartyVatNumber ??= c.VatNumber; invoice.PartyCrNumber ??= c.CrNumber; invoice.PartyIdType ??= c.IdType; invoice.PartyIdNumber ??= c.IdNumber;
                invoice.PartyPhone ??= c.Phone; invoice.PartyEmail ??= c.Email;
                // العنوان الوطني من بطاقة العميل ما لم يُرسل مع الفاتورة
                invoice.PartyStreet ??= c.Street; invoice.PartyBuildingNo ??= c.BuildingNo; invoice.PartyDistrict ??= c.District;
                invoice.PartyCity ??= c.City; invoice.PartyPostalCode ??= c.PostalCode; invoice.PartyAdditionalNo ??= c.AdditionalNo;
                if (string.IsNullOrWhiteSpace(invoice.PartyCountry)) invoice.PartyCountry = c.Country;
            }
            else
            {
                var s = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.SupplierNotFound);
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = s.NameAr;
                invoice.PartyVatNumber ??= s.VatNumber; invoice.PartyCrNumber ??= s.CrNumber; invoice.PartyIdType ??= s.IdType; invoice.PartyIdNumber ??= s.IdNumber;
                invoice.PartyPhone ??= s.Phone; invoice.PartyEmail ??= s.Email;
                invoice.PartyStreet ??= s.Street; invoice.PartyBuildingNo ??= s.BuildingNo; invoice.PartyDistrict ??= s.District;
                invoice.PartyCity ??= s.City; invoice.PartyPostalCode ??= s.PostalCode; invoice.PartyAdditionalNo ??= s.AdditionalNo;
                if (string.IsNullOrWhiteSpace(invoice.PartyCountry)) invoice.PartyCountry = s.Country;
            }
        }
        if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = isSales ? DefaultCashCustomer : "مورد نقدي";
        // المستودع: المحدَّد في الفاتورة أو الافتراضي للمنشأة
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId, ct);
        if (r.WarehouseId.HasValue && warehouse.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
        invoice.WarehouseId = warehouse.Id; invoice.WarehouseName = warehouse.NameAr;

        invoice.PartyCountry = string.IsNullOrWhiteSpace(invoice.PartyCountry) ? "SA" : invoice.PartyCountry.Trim().ToUpperInvariant();
        var domesticParty = invoice.PartyCountry == "SA";
        if (domesticParty && !string.IsNullOrWhiteSpace(invoice.PartyVatNumber) && !SaudiVat.IsValid(invoice.PartyVatNumber))
            throw new ValidationFailedException(Messages.PartyVatNumberInvalid);
        // فاتورة بيع المعارض: نوعها من الحالة الضريبية للعميل لا من الشاشة — ضريبية (B2B) للمسجَّل، ومبسطة (B2C) لغيره
        // وللبيع بلا عميل (الافتراضي)
        if (showroom && r.Kind == InvoiceKind.Sales) invoice.InvoiceType = vatRegisteredCustomer ? InvoiceType.TaxInvoice : InvoiceType.Simplified;
        if (r.Kind == InvoiceKind.Sales && invoice.InvoiceType == InvoiceType.TaxInvoice) ValidateTaxInvoiceBuyer(invoice, domesticParty);

        // الدفع: الطريقة المختارة تُعتمد (فعّالة) ونوعها يحدّد نوع الدفع. المرتجع يرث طرق فاتورته (وطريقة الرد تُعتمد في CreateReturnAsync).
        if (!isReturn && !r.IsSplitPayment)
            invoice.PaymentMethod = await PaymentMethodRules.MethodForAsync(_db, r.PaymentMethod, r.PaymentMethodCode, ct);
        invoice.PaymentSplits.Clear();
        if (r.IsSplitPayment)
        {
            if (r.PaymentSplits.Count == 0 || r.PaymentSplits.Any(s => s.Amount <= 0))
                throw new ValidationFailedException(Messages.SplitPaymentsNeedPositiveAmounts);
            if (Math.Abs(r.PaymentSplits.Sum(s => s.Amount) - invoice.GrandTotal) > 0.005m)
                throw new ValidationFailedException(Messages.SplitPaymentsMustEqualInvoiceTotal);
            foreach (var s in r.PaymentSplits)
            {
                var method = isReturn ? s.Method : await PaymentMethodRules.MethodForAsync(_db, s.Method, s.PaymentMethodCode, ct);
                invoice.PaymentSplits.Add(new InvoicePaymentSplit { Method = method, PaymentMethodCode = s.PaymentMethodCode, Amount = s.Amount, Reference = s.Reference });
            }
        }
        var hasCredit = invoice.PaymentMethod == PaymentMethod.Credit || invoice.PaymentSplits.Any(s => s.Method == PaymentMethod.Credit);
        if (hasCredit && !r.PartyId.HasValue)
            throw new ValidationFailedException(isSales ? Messages.CreditSaleRequiresCustomer : Messages.CreditPurchaseRequiresSupplier);

        if (hasCredit && r.Kind == InvoiceKind.Sales)
        {
            var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(x => x.Id == r.PartyId, ct);
            var creditPart = (invoice.PaymentMethod == PaymentMethod.Credit && !r.IsSplitPayment
                ? invoice.GrandTotal : invoice.PaymentSplits.Where(s => s.Method == PaymentMethod.Credit).Sum(s => s.Amount)) * invoice.ExchangeRate;
            if (customer.CreditLimit > 0 && customer.CurrentBalance + creditPart > customer.CreditLimit)
                throw new ConflictException(string.Format(Messages.CreditLimitExceeded, customer.CreditLimit, customer.CurrentBalance));
        }

        // المرتجع: تحقّق من الفاتورة الأصلية (في جدول المعارض للمعارض، وفي جدول نوعها للتجارة العامة)
        if (isReturn)
        {
            var orig = await _db.Set<InvoiceBase>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, ct)
                ?? throw new ValidationFailedException(Messages.OriginalInvoiceNotFoundDot);
            var expected = r.Kind == InvoiceKind.SalesReturn ? InvoiceKind.Sales : InvoiceKind.Purchase;
            var expectedTable = showroom ? typeof(Invoice) : expected == InvoiceKind.Sales ? typeof(SalesInvoice) : typeof(PurchaseInvoice);
            if (orig.Kind != expected || orig.Status != "posted" || orig.GetType() != expectedTable) throw new ConflictException(Messages.OriginalInvoiceNotReturnable);
            invoice.OriginalInvoiceNumber = orig.InvoiceNumber;
            invoice.Module = orig.Module;
        }
        return invoice;
    }

    /// <summary>
    /// معالجة الفاتورة غير الأساسية (صفري / معفى / صادرات) تتغلّب على تصنيف كل سطر: الأسطر كلها بنسبة 0 وتصنيف المعالجة
    /// وسببها، ويسقط مبلغ الضريبة الثابت (هامش الربح). تُطبَّق بعد اشتقاق أسطر السيارات وافتراضات الأصناف وقبل التسعير،
    /// فالفاتورة والقيد والإقرار ورمز الهيئة كلها من الأسطر المحفوظة بعد الفرض.
    /// </summary>
    /// <summary>
    /// اختيار معالجة غير أساسية يتجاوز تصنيف كل الأصناف دفعة واحدة، فيحتاج صلاحية تجاوز التصنيف الضريبي نفسها؛
    /// المرتجع يرث معالجة أصله، وتعديل فاتورة بمعالجتها المحفوظة نفسها لا يُعدّ تجاوزاً جديداً.
    /// </summary>
    private async Task EnsureVatTreatmentAllowedAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.VatTreatment == InvoiceVatTreatment.Standard || r.OriginalInvoiceId.HasValue) return;
        if (r.ExistingInvoiceId is Guid id
            && await _db.Set<InvoiceBase>().AsNoTracking().AnyAsync(i => i.Id == id && i.VatTreatment == r.VatTreatment, ct)) return;
        if (!await _permissions.HasPermissionAsync(PermissionService.InvoiceTaxTreatmentOverrideScreenId, ScreenAction.Edit, ct))
            throw new ForbiddenException(Messages.InvoiceTaxTreatmentOverrideForbidden);
    }

    private static void ApplyDocumentVatTreatment(CreateInvoiceDto r, List<string> errors)
    {
        if (!Enum.IsDefined(r.VatTreatment)) { errors.Add(Messages.VatTreatmentInvalid); return; }
        if (r.VatTreatment == InvoiceVatTreatment.Standard) { r.VatTreatmentReasonCode = null; return; }

        var category = r.VatTreatment == InvoiceVatTreatment.Exempt ? VatCategory.Exempt : VatCategory.ZeroRated;
        var reason = VatExemptionReasons.Find(r.VatTreatmentReasonCode)
            ?? (r.VatTreatment == InvoiceVatTreatment.Export ? VatExemptionReasons.Find("VATEX-SA-32") : null);
        if (reason != null && (reason.Category != category || (r.VatTreatment == InvoiceVatTreatment.Export && !reason.IsExport)))
        { errors.Add(Messages.VatTreatmentReasonMismatch); return; }
        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        if (isSales && reason == null) { errors.Add(Messages.VatTreatmentReasonRequired); return; }

        r.VatTreatmentReasonCode = reason?.Code;
        foreach (var item in r.Items)
        {
            item.VatCategory = category;
            item.VatRate = 0;
            item.VatAmountOverride = null;
            item.VatOverrideReason = null; // قرار على مستوى المستند لا تجاوز لتصنيف صنف
            item.VatExemptionReasonCode = reason?.Code;
        }
    }

    /// <summary>
    /// سبب الإعفاء: إن أُرسل فهو رمز معروف يطابق تصنيف سطره؛ وفي فاتورة المبيعات يلزم لكل سطر حُدِّد له تصنيف غير أساسي
    /// (السطر بنسبة 0 بلا تصنيف — كبيع نقطة البيع لصنف صفري — يبقى صفرياً بلا سبب).
    /// </summary>
    private static void ValidateExemptionReasons(CreateInvoiceDto r, List<string> errors)
    {
        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        foreach (var item in r.Items)
        {
            var reason = VatExemptionReasons.Find(item.VatExemptionReasonCode);
            if (!string.IsNullOrWhiteSpace(item.VatExemptionReasonCode) && reason == null) { errors.Add(Messages.VatExemptionReasonUnknown); return; }
            var category = item.VatCategory ?? (reason?.Category);
            if (reason != null && category != reason.Category) { errors.Add(Messages.VatExemptionReasonCategoryMismatch); return; }
            if (reason != null) item.VatCategory ??= reason.Category;
            if (isSales && reason == null && !r.IsReturn && item.VatCategory is VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.OutOfScope)
            { errors.Add(Messages.VatExemptionReasonRequired); return; }
        }
    }

    /// <summary>
    /// الفاتورة الضريبية (B2B) تحمل بيانات المشتري الإلزامية: اسمه، رقمه الضريبي (للمشتري داخل المملكة)، وعنوانه:
    /// الشارع والمدينة دائماً، ورقم المبنى (4 أرقام) والحي والرمز البريدي (5 أرقام) للعنوان الوطني السعودي.
    /// </summary>
    private static void ValidateTaxInvoiceBuyer(InvoiceBase invoice, bool domestic)
    {
        var missing = new List<string>();
        if (!invoice.PartyId.HasValue && invoice.PartyName == DefaultCashCustomer) missing.Add(Messages.BuyerFieldName);
        if (domestic && string.IsNullOrWhiteSpace(invoice.PartyVatNumber)) missing.Add(Messages.BuyerFieldVatNumber);
        if (string.IsNullOrWhiteSpace(invoice.PartyStreet)) missing.Add(Messages.BuyerFieldStreet);
        if (string.IsNullOrWhiteSpace(invoice.PartyCity)) missing.Add(Messages.BuyerFieldCity);
        if (domestic)
        {
            if (!IsDigits(invoice.PartyBuildingNo, 4)) missing.Add(Messages.BuyerFieldBuildingNo);
            if (string.IsNullOrWhiteSpace(invoice.PartyDistrict)) missing.Add(Messages.BuyerFieldDistrict);
            if (!IsDigits(invoice.PartyPostalCode, 5)) missing.Add(Messages.BuyerFieldPostalCode);
        }
        if (missing.Count == 0) return;
        var message = string.Format(Messages.TaxInvoiceBuyerDataRequired, string.Join("، ", missing));
        throw new ValidationFailedException(message, missing.Prepend(message));
    }

    private static bool IsDigits(string? value, int length) => value?.Trim() is { } v && v.Length == length && v.All(char.IsAsciiDigit);

    // ---------------- الترحيل: مخزون + قيد + QR ----------------
    /// <param name="preview">معاينة القيد: يتخطى إنشاء/بيع المركبات و QR (لا أثر لهما على القيد) ليُسمح بمعاينة مسودة ناقصة الشواسيه.</param>
    private async Task FinalizeAsync(InvoiceBase invoice, CancellationToken ct, bool preview = false)
    {
        if (invoice.InvoiceNumber.StartsWith(DraftPrefix, StringComparison.Ordinal))
            invoice.InvoiceNumber = invoice.ReferenceType == "pos_transaction" && invoice.Kind == InvoiceKind.Sales
                ? await _numbers.NextAsync("pos_invoice", "POS-", ct) // ترقيم نقاط البيع مستقل ومتسلسل
                : await _numbers.NextAsync(KeyFor(invoice.Kind), PrefixFor(invoice.Kind), ct);
        if (!preview && invoice is Invoice car)
            foreach (var ext in _extensions) await ext.OnPostingAsync(car, ct); // سجلات الوحدات (إنشاء المركبات وبيعها...)
        var isSales = invoice.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);

        // الدفاتر والمخزون بالعملة الأساسية: مبالغ المستند تُحوَّل بسعر صرفه (1 للعملة الأساسية)
        var fx = invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate;
        var baseTotal = DocumentPricing.Round(invoice.GrandTotal * fx);
        var baseVat = DocumentPricing.Round(invoice.VatTotal * fx);
        var baseNet = baseTotal - baseVat;
        var payments = ToBase(await ResolvePaymentsAsync(invoice, ct), fx, invoice.GrandTotal, baseTotal);
        decimal totalCost = 0;
        var stockByCenter = new CostCenterTotals(); // تغيّر قيمة المخزون لكل مركز تكلفة من أسطره

        Dictionary<Guid, decimal> originalCosts = new();
        if (invoice.Kind == InvoiceKind.SalesReturn && invoice.OriginalInvoiceId.HasValue)
            originalCosts = (await _db.Set<InvoiceItemBase>().AsNoTracking().Where(i => i.InvoiceId == invoice.OriginalInvoiceId).ToListAsync(ct))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First().UnitCost);

        foreach (var item in invoice.Lines.Where(i => i.ItemId != Guid.Empty))
        {
            var movement = invoice.Kind switch
            {
                InvoiceKind.Sales => new RecordStockMovementDto { Type = StockMovementType.OutSales },
                InvoiceKind.Purchase => new RecordStockMovementDto { Type = StockMovementType.InPurchase, UnitCost = item.Quantity == 0 ? 0 : Math.Round(item.TotalBeforeVat * fx / item.Quantity, 4) },
                InvoiceKind.SalesReturn => new RecordStockMovementDto { Type = StockMovementType.AdjustmentIn, UnitCost = originalCosts.GetValueOrDefault(item.ItemId) },
                _ => new RecordStockMovementDto { Type = StockMovementType.AdjustmentOut },
            };
            movement.ItemId = item.ItemId; movement.Quantity = item.Quantity;
            movement.UnitPrice = item.UnitPrice; movement.ReferenceNumber = invoice.InvoiceNumber;
            movement.Date = invoice.IssueDate;
            movement.SourceType = "invoice"; movement.SourceId = invoice.Id;
            movement.WarehouseId = invoice.WarehouseId;

            var recorded = await _inventory.RecordMovementAsync(movement, ct);
            item.UnitCost = invoice.Kind == InvoiceKind.Purchase ? movement.UnitCost : recorded.UnitCost;
            // تغيّر القيمة الدفترية للصنف لا الكمية × التكلفة: حساب المخزون يبقى مساوياً لقيمة الأصناف بلا هللات تقريب
            // بإشارته: الإضافة بآخر سعر شراء أقل تُنقص قيمة الرصيد كله
            var valueChange = invoice.Kind is InvoiceKind.Purchase or InvoiceKind.SalesReturn ? recorded.ValueChange : -recorded.ValueChange;
            totalCost += valueChange;
            stockByCenter.Add(item.CostCenterId, valueChange);
        }
        var stockCost = totalCost; // قيمة ما دخل/خرج من المخزون فعلاً (الأصناف المخزنية فقط)
        var costByCenter = stockByCenter.Copy(); // تكلفة المبيعات لكل مركز: مخزون أسطره ثم تكلفة بنود خدماته
        foreach (var svc in invoice.Lines.Where(i => i.ItemId == Guid.Empty && i.UnitCost > 0))
        {
            var svcCost = Math.Round(svc.Quantity * svc.UnitCost, 2);
            totalCost += svcCost;
            costByCenter.Add(svc.CostCenterId, svcCost);
        }
        invoice.TotalCost = totalCost; // بالعملة الأساسية
        invoice.GrossProfit = isSales ? (invoice.IsReturn ? -(baseNet - totalCost) : baseNet - totalCost) : 0;

        // الجرد الدوري: الأصناف تُقيَّم في بضاعة آخر المدة، فحساب مخزون يدوي على مستند أصناف يُثبتها مرتين (السيارات خارجه)
        if (!string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) && invoice is not Invoice { VehicleLines.Count: > 0 }
            && invoice.Lines.Any(i => i.ItemId != Guid.Empty) && (await PostingAccounts.SettingsAsync(_db, ct)).IsPeriodic(invoice.IssueDate))
            throw new ValidationFailedException(Messages.PeriodicManualInventoryAccount);

        // مشتريات: البنود غير المخزنية (خدمات/مصروفات) لا تدخل حساب المخزون، إلا إن حدّد المستند حساب مخزون صراحةً
        // (شراء السيارات: بنودها وصفية وهي مخزون فعلاً).
        var expenseAmount = isSales || !string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
            : Math.Min(baseNet, DocumentPricing.Round(invoice.Lines.Where(i => i.ItemId == Guid.Empty).Sum(i => i.TotalBeforeVat) * fx));
        if (expenseAmount > 0) await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.PurchasedServices);
        // المخزون بتغيّر قيمته الدفترية لا بسعر المستند: مرتجع المشتريات يخرج بتكلفته الحالية، والشراء بالمعيارية يدخل بها،
        // وما زاد أو نقص عن صافي المستند فرق سعر. البنود غير المخزنية على حساب مخزون صريح تدخله بصافيها.
        decimal? inventoryAmount = null;
        string? varianceAccount = null;
        var hasVehicleLines = invoice is Invoice { VehicleLines.Count: > 0 };
        if (!isSales && !hasVehicleLines && invoice.Lines.Any(i => i.ItemId != Guid.Empty))
        {
            var nonStockNet = string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
                : DocumentPricing.Round(invoice.Lines.Where(i => i.ItemId == Guid.Empty).Sum(i => i.TotalBeforeVat) * fx);
            inventoryAmount = stockCost + nonStockNet;
            if (inventoryAmount != baseNet - expenseAmount)
            {
                var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct);
                varianceAccount = policy is { Method: CostingMethod.Standard } && !string.IsNullOrWhiteSpace(policy.StandardCostVarianceAccountCode)
                    ? policy.StandardCostVarianceAccountCode.Trim() : DefaultAccounts.PurchasePriceVariance;
                await DefaultAccounts.EnsureAsync(_db, ct, varianceAccount);
            }
        }

        // القيد المحاسبي عبر المحرك المركزي
        var partyAccount = await PartyAccountAsync(invoice, isSales, ct);
        var description = $"{DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} - {invoice.PartyName}";
        PostingResult posted;
        if (isSales)
            posted = await _posting.PostSaleAsync(new SalePostingRequest
            {
                Module = invoice.Module, Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id, ReplaceEntryId = invoice.JournalEntryId,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, ExciseTaxAmount = DocumentPricing.Round(invoice.ExciseTaxTotal * fx),
                VatAmount = baseVat, CostAmount = totalCost, Payments = payments,
                RevenueAccountCode = invoice.RevenueAccountCode, InventoryAccountCode = invoice.InventoryAccountCode, CogsAccountCode = invoice.CogsAccountCode, WarehouseId = invoice.WarehouseId,
                RevenueLines = RevenueByLine(invoice, fx, baseNet),
                CostLines = costByCenter.ToAmounts(),
            }, ct);
        else
            posted = await _posting.PostPurchaseAsync(new PurchasePostingRequest
            {
                Module = invoice.Module, Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id, ReplaceEntryId = invoice.JournalEntryId,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, VatAmount = baseVat, Payments = payments,
                ExpenseAmount = expenseAmount, InventoryAmount = inventoryAmount, VarianceAccountCode = varianceAccount,
                InventoryAccountCode = invoice.InventoryAccountCode, WarehouseId = invoice.WarehouseId,
                CostCenterShares = PurchaseShares(invoice, fx, baseNet, expenseAmount, inventoryAmount, stockByCenter),
            }, ct);
        invoice.JournalEntryId = posted.JournalEntryId;

        if (isSales && !preview)
            invoice.ZatcaQrCode = _zatca.GenerateTlvQr(tenant.NameAr, tenant.VatNumber,
                invoice.IssueDate.Date.Add(TimeSpan.TryParse(invoice.IssueTime, out var t) ? t : TimeSpan.Zero), invoice.GrandTotal, invoice.VatTotal);

        invoice.Status = "posted";
        await _db.SaveChangesAsync(ct);
        if (!preview)
            await _audit.LogAsync("INVOICE_POSTED", invoice.GetType().Name, invoice.Id.ToString(),
                $"ترحيل {DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} بإجمالي {invoice.GrandTotal:0.00} {invoice.CurrencyCode} - {invoice.PartyName}", ct);
    }

    private async Task<List<PaymentPosting>> ResolvePaymentsAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var methods = await _db.Set<PaymentMethodItem>().AsNoTracking().ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(invoice.SettlementAccountCode)) // تحصيل عبر بوابة الدفع: كل الجزء غير الآجل لحساب التسوية
        {
            var paid = invoice.PaymentSplits.Count > 0 ? invoice.PaymentSplits.Where(s => s.Method != PaymentMethod.Credit).Sum(s => s.Amount)
                : invoice.PaymentMethod == PaymentMethod.Credit ? 0 : invoice.GrandTotal;
            return paid > 0 ? new() { new PaymentPosting(invoice.SettlementAccountCode, paid) } : new();
        }
        // الدفع بعربون سيارة يقفل حساب عربونات العملاء المحدَّد في إعدادات المعرض
        var byDeposit = invoice.PaymentMethod == PaymentMethod.CustomerDeposit || invoice.PaymentSplits.Any(s => s.Method == PaymentMethod.CustomerDeposit);
        var deposits = byDeposit ? (await ERP.Service.Services.CarShowroom.SalesCharges.SettingsAsync(_db, ct)).CustomerDepositsAccount : null;
        if (invoice.PaymentSplits.Count > 0)
            return invoice.PaymentSplits.Where(s => s.Method != PaymentMethod.Credit)
                .Select(s => new PaymentPosting(TreasuryResolver.Resolve(s.Method, methods, deposits, s.PaymentMethodCode), s.Amount)).ToList();
        if (invoice.PaymentMethod == PaymentMethod.Credit || invoice.GrandTotal <= 0) return new();
        return new() { new PaymentPosting(TreasuryResolver.Resolve(invoice.PaymentMethod, methods, deposits, invoice.PaymentMethodCode), invoice.GrandTotal) };
    }

    /// <summary>صافي كل سطر بالعملة الأساسية؛ كسر التقريب على آخر سطر ليطابق صافي المستند.</summary>
    private static List<(InvoiceItemBase Line, decimal Net)> LineNets(InvoiceBase invoice, decimal fx, decimal baseNet)
    {
        var lines = invoice.Lines.Select(i => (Line: i, Net: DocumentPricing.Round(i.TotalBeforeVat * fx))).ToList();
        if (lines.Count > 0) lines[^1] = (lines[^1].Line, baseNet - lines.Take(lines.Count - 1).Sum(l => l.Net));
        return lines;
    }

    /// <summary>إيراد كل سطر على حسابه ومركز تكلفته بالعملة الأساسية.</summary>
    private static List<RevenuePosting> RevenueByLine(InvoiceBase invoice, decimal fx, decimal baseNet)
        => LineNets(invoice, fx, baseNet).Select(l => new RevenuePosting(l.Line.RevenueAccountCode, l.Line.CostCenterId, l.Net)).ToList();

    /// <summary>
    /// حصص مراكز التكلفة في قيد المشتريات: صافي كل مركز من أسطره، وخدماته بنسبة صافي بنود خدماته، ومخزونه تغيّر قيمة أصنافه
    /// الفعلي (ومعه بنوده غير المخزنية حين يُحدَّد حساب مخزون صريح). مجاميع الحصص = مبالغ المستند تماماً، وفرق السعر لكل مركز ما يبقى.
    /// </summary>
    private static List<PurchaseCostCenterShare> PurchaseShares(InvoiceBase invoice, decimal fx, decimal baseNet, decimal expenseAmount,
        decimal? inventoryAmount, CostCenterTotals stockByCenter)
    {
        var net = new CostCenterTotals();
        var nonStock = new CostCenterTotals();
        foreach (var (line, amount) in LineNets(invoice, fx, baseNet))
        {
            net.Add(line.CostCenterId, amount);
            if (line.ItemId == Guid.Empty) nonStock.Add(line.CostCenterId, amount);
        }
        var expense = CostCenterTotals.Apportion(expenseAmount, nonStock);
        CostCenterTotals? inventory = null;
        if (inventoryAmount is { } total)
        {
            inventory = stockByCenter.Copy();
            var nonStockInventory = CostCenterTotals.Apportion(total - stockByCenter.Total, nonStock);
            foreach (var center in nonStockInventory.Centers) inventory.Add(center, nonStockInventory[center]);
        }
        return net.Centers.Union(expense.Centers).Union(inventory?.Centers ?? Enumerable.Empty<Guid?>())
            .Select(c => new PurchaseCostCenterShare(c, net[c], expense[c], inventory?[c])).ToList();
    }

    /// <summary>يحوّل المدفوع إلى العملة الأساسية؛ السداد الكامل يطابق إجمالي المستند المحوَّل بلا كسر تقريب.</summary>
    private static List<PaymentPosting> ToBase(List<PaymentPosting> payments, decimal fx, decimal documentTotal, decimal baseTotal)
    {
        if (fx == 1 || payments.Count == 0) return payments;
        var converted = payments.Select(p => p with { Amount = DocumentPricing.Round(p.Amount * fx) }).ToList();
        if (payments.Sum(p => p.Amount) == documentTotal)
            converted[^1] = converted[^1] with { Amount = baseTotal - converted.Take(converted.Count - 1).Sum(p => p.Amount) };
        return converted;
    }

    private async Task<string?> PartyAccountAsync(InvoiceBase invoice, bool isSales, CancellationToken ct)
    {
        if (!invoice.PartyId.HasValue) return null;
        return isSales
            ? await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == invoice.PartyId).Select(c => c.AccountCode).FirstOrDefaultAsync(ct)
            : await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == invoice.PartyId).Select(s => s.AccountCode).FirstOrDefaultAsync(ct);
    }

    /// <summary>بادئة الرقم المؤقت للمسودة قبل صرف رقمها المتسلسل عند الترحيل.</summary>
    private const string DraftPrefix = "DRAFT-";
    private const string DefaultCashCustomer = "عميل نقدي";

    private static string KeyFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "sales_invoice", InvoiceKind.Purchase => "purchase_invoice",
        InvoiceKind.SalesReturn => "sales_return", _ => "purchase_return",
    };

    private static string PrefixFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "SINV-", InvoiceKind.Purchase => "PINV-", InvoiceKind.SalesReturn => "SRET-", _ => "PRET-",
    };

    private static string SourceTypeFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "sales", InvoiceKind.Purchase => "purchase", InvoiceKind.SalesReturn => "sales_return", _ => "purchase_return",
    };

    private static string DescribeKind(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "فاتورة مبيعات", InvoiceKind.Purchase => "فاتورة مشتريات",
        InvoiceKind.SalesReturn => "مرتجع مبيعات", _ => "مرتجع مشتريات",
    };
}
