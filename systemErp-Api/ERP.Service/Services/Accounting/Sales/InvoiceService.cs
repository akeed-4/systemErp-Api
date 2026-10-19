using System.Text;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class InvoiceService : IInvoiceService
{
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;
    private readonly IZatcaService _zatca;
    private readonly ITransactionRunner _tx;
    private readonly ERP.Core.Contracts.Shared.IAuditService _audit;
    /// <summary>امتدادات الوحدات (المعارض...) على دورة الفاتورة: منطقها خارج هذه الخدمة.</summary>
    private readonly IReadOnlyList<IInvoiceExtension> _extensions;

    public InvoiceService(ErpDbContext db, IAccountingPostingService posting, IInventoryService inventory,
        INumberSequenceService numbers, IZatcaService zatca, ITransactionRunner tx, ERP.Core.Contracts.Shared.IAuditService audit,
        IEnumerable<IInvoiceExtension> extensions)
    {
        _audit = audit; _extensions = extensions.ToList();
        _db = db; _posting = posting; _inventory = inventory; _numbers = numbers; _zatca = zatca; _tx = tx;
    }

    // ---------------- القراءة ----------------
    public async Task<PagedResult<InvoiceDto>> ListAsync(InvoiceKind? kind, PaginationParams p, bool? hasVehicleLines = null, CancellationToken ct = default)
    {
        var q = _db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (hasVehicleLines.HasValue) q = hasVehicleLines.Value ? q.Where(i => i.VehicleLines.Any()) : q.Where(i => !i.VehicleLines.Any());
        if (!string.IsNullOrWhiteSpace(p.Status)) q = q.Where(i => i.Status == p.Status);
        if (p.StartDate.HasValue) q = q.Where(i => i.IssueDate >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(i => i.IssueDate <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(i => i.InvoiceNumber.Contains(t) || i.PartyName.Contains(t) || (i.PartyVatNumber != null && i.PartyVatNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).Include(i => i.VehicleBatches)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).AsSplitQuery().ToListAsync(ct);
        var due = await InvoiceBalances.ForAsync(_db, items.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, ct);
        return new PagedResult<InvoiceDto>
        {
            Items = await MaskAsync(items.Select(i => (i, ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0))).ToList(), ct),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL، والمتبقي يُحسب لفواتير الصفحة فقط.</summary>
    public Task<LoadResult> LoadAsync(InvoiceKind? kind, DataSourceLoadOptions options, bool? hasVehicleLines = null, CancellationToken ct = default)
    {
        var q = _db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (hasVehicleLines.HasValue) q = hasVehicleLines.Value ? q.Where(i => i.VehicleLines.Any()) : q.Where(i => !i.VehicleLines.Any());
        return EntityLoader.LoadAsync(q.Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).Include(i => i.VehicleBatches).AsSplitQuery(), options, async (page, token) =>
        {
            var due = await InvoiceBalances.ForAsync(_db, page.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, token);
            return await MaskAsync(page.Select(i => (i, ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0))).ToList(), token);
        }, ct, EntityLoader.Desc(nameof(Invoice.IssueDate)), EntityLoader.Desc(nameof(Invoice.InvoiceNumber)));
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).Include(i => i.VehicleBatches).AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(Messages.InvoiceNotFound);
        var dto = ToDto(invoice, invoice.Status == "posted" ? await InvoiceBalances.DueAsync(_db, id, ct) : 0);
        foreach (var ext in _extensions) await ext.FillAsync(invoice, dto, ct);
        return (await MaskAsync(new() { (invoice, dto) }, ct))[0];
    }

    private static InvoiceDto ToDto(Invoice invoice, decimal amountDue)
    {
        var dto = Mapper.Map<InvoiceDto>(invoice);
        dto.AmountDue = amountDue;
        return dto;
    }

    private async Task<List<InvoiceDto>> MaskAsync(List<(Invoice Invoice, InvoiceDto Dto)> rows, CancellationToken ct)
    {
        foreach (var ext in _extensions) await ext.MaskAsync(rows, ct);
        return rows.Select(x => x.Dto).ToList();
    }

    // ---------------- الإنشاء ----------------
    public async Task<InvoiceDto> CreateAsync(CreateInvoiceDto r, CancellationToken ct = default)
        => await SendToZatcaIfRequestedAsync(await _tx.RunAsync(async token =>
        {
            r.ExistingInvoiceId = null;
            var invoice = await BuildAsync(r, token);
            var draft = string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
            invoice.Status = "draft";
            // المسودة برقم مؤقت؛ الرقم المتسلسل يُصرف عند الترحيل فلا يترك حذف مسودة فجوة في تسلسل الفواتير
            invoice.InvoiceNumber = DraftPrefix + invoice.Id.ToString("N")[..10].ToUpperInvariant();
            _db.Add(invoice);
            foreach (var ext in _extensions) await ext.OnSavingAsync(invoice, deleting: false, token);
            await _db.SaveChangesAsync(token);
            if (!draft) await FinalizeAsync(invoice, token);
            return await GetAsync(invoice.Id, token);
        }, ct), ct);

    public async Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto r, CancellationToken ct = default)
        => await SendToZatcaIfRequestedAsync(await _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).Include(i => i.VehicleBatches).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
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
            var fresh = await BuildAsync(r, token); // نفس تحقق الإنشاء وحساب الأرقام في السرفر
            var (keepId, keepTenant, keepCreated, keepNumber, keepUuid) = (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid);
            var (keepRefType, keepRefId) = (invoice.ReferenceType, invoice.ReferenceId); // ارتباط المرحّلة بمستندها التجاري يبقى
            var keepEntry = invoice.JournalEntryId; // قيد المرحّلة يُستبدل في مكانه عند إعادة الترحيل

            _db.RemoveRange(invoice.Items);
            _db.RemoveRange(invoice.PaymentSplits);
            _db.RemoveRange(invoice.VehicleLines);
            _db.RemoveRange(invoice.VehicleBatches);
            invoice.Items.Clear();
            invoice.PaymentSplits.Clear();
            invoice.VehicleLines.Clear();
            invoice.VehicleBatches.Clear();
            Mapper.Apply(fresh, invoice);
            (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid) = (keepId, keepTenant, keepCreated, keepNumber, keepUuid);
            if (wasPosted) (invoice.ReferenceType, invoice.ReferenceId, invoice.JournalEntryId) = (keepRefType, keepRefId, keepEntry);
            invoice.Status = "draft";
            foreach (var item in fresh.Items) invoice.Items.Add(item);
            foreach (var split in fresh.PaymentSplits) invoice.PaymentSplits.Add(split);
            foreach (var vl in fresh.VehicleLines) invoice.VehicleLines.Add(vl);
            foreach (var batch in fresh.VehicleBatches)
            {
                invoice.VehicleBatches.Add(batch);
                _db.Add(batch); // معرّفها محدَّد مسبقاً (تشير إليه شواصيها)، فتُضاف صراحةً
            }
            foreach (var ext in _extensions) await ext.OnSavingAsync(invoice, deleting: false, token);
            await _db.SaveChangesAsync(token);

            // المرحّلة تعود مرحّلة دائماً (بنفس رقمها) بقيد وحركات مخزنية جديدة
            if (wasPosted || !string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase)) await FinalizeAsync(invoice, token);
            if (wasPosted)
                await _audit.LogAsync("INVOICE_POSTED_EDITED", nameof(Invoice), invoice.Id.ToString(),
                    $"تعديل {DescribeKind(invoice.Kind)} مرحّلة {invoice.InvoiceNumber}: عُكس قيدها وحركاتها وأُعيد ترحيلها بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            return await GetAsync(id, token);
        }, ct), ct);

    /// <summary>
    /// يلغي أثر ترحيل فاتورة مبيعات/مشتريات تمهيداً لتعديلها وإعادة ترحيلها: يعكس قيدها (بتاريخه، في فترة مفتوحة)
    /// ويعكس حركاتها المخزنية (المبيعات تعود بتكلفة خروجها، والمشتريات تخرج بتكلفتها). يُمنع ما لا يُصحَّح إلا بمرتجع:
    /// المرسلة للهيئة، المرتجعات وما عليه مرتجعات أو سندات موزّعة، فواتير السيارات ومستندات النقاط والعقود،
    /// والمشتريات التي تلتها حركات على أصنافها (يتغيّر بها المتوسط) أو بطريقة الوارد أولاً.
    /// </summary>
    private async Task UnpostForEditAsync(Invoice invoice, CancellationToken ct, bool forDelete = false)
    {
        if (invoice.Kind is not (InvoiceKind.Sales or InvoiceKind.Purchase) || invoice.IsReturn || invoice.OriginalInvoiceId.HasValue)
            throw new ConflictException(Messages.PostedInvoiceLocked);
        if (invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted) throw new ConflictException(Messages.PostedInvoiceEditZatcaSubmitted);
        // فاتورة نقطة بيع/عقد: مجاميع مستندها المصدر (الوردية، العقد) تتبعها، فتُعدَّل من ذلك المستند
        if (invoice.ReferenceType is not (null or "quotation" or "sales_order" or "purchase_order" or "material_requisition"))
            throw new ConflictException(Messages.InvoiceFromSourceDocument);

        var stockItems = invoice.Items.Where(i => i.ItemId != Guid.Empty).ToList();
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
            if (await _db.Set<Invoice>().AnyAsync(i => i.OriginalInvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedInvoiceDeleteHasReturns);
            if (await _db.Set<VoucherAllocation>().AnyAsync(a => a.InvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedInvoiceDeleteHasPayments);
        }
        foreach (var ext in _extensions) await ext.OnUnpostingAsync(invoice, ct);
        // التعديل: يبقى القيد ويُستبدل في مكانه بنفس رقمه عند إعادة الترحيل (FinalizeAsync). الحذف: يُعكس القيد فيبقى أثره في السجل.
        if (forDelete && invoice.JournalEntryId is { } entryId) await _posting.ReverseAsync(entryId, reason, ct);

        // أثر الفاتورة الأصلي على قيمة المخزون (ما رُحِّل لحساب المخزون): تكلفتها ناقص تكلفة بنود الخدمات
        var serviceCost = invoice.Items.Where(i => i.ItemId == Guid.Empty && i.UnitCost > 0).Sum(i => Math.Round(i.Quantity * i.UnitCost, 2));
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
        await InventoryValuation.PostRevaluationAsync(_db, _posting, reversalChange + originalStockChange, reason, invoice.Id, ct);
        if (forDelete) invoice.JournalEntryId = null;
    }

    public async Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default)
        => await SendToZatcaIfRequestedAsync(await _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).Include(i => i.VehicleBatches).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (invoice.Status != "draft") throw new ConflictException(Messages.InvoiceAlreadyPostedOrCancelled);
            // سير الموافقات: المسودة المعلّقة أو المرفوضة لا تُرحَّل (آخر طلب على المستند هو المرجع)
            var approval = await _db.Set<ApprovalRequest>().Where(r => r.DocumentId == id)
                .OrderByDescending(r => r.CreatedAt).Select(r => r.Status).FirstOrDefaultAsync(token);
            if (approval == "pending") throw new ConflictException(Messages.InvoicePendingApproval);
            if (approval == "rejected") throw new ConflictException(Messages.InvoiceApprovalRejected);
            await FinalizeAsync(invoice, token);
            return await GetAsync(id, token);
        }, ct), ct);

    /// <summary>
    /// الإرسال لهيئة الزكاة بعد اكتمال الترحيل (خارج معاملته): لفاتورة مبيعات مرحّلة طُلب إرسالها من الشاشة أو من
    /// إعداد المنشأة. رفض الهيئة أو تعذّر الاتصال لا يُسقط الفاتورة: تبقى مرحّلة والسبب محفوظ عليها ويُعاد الإرسال.
    /// </summary>
    private async Task<InvoiceDto> SendToZatcaIfRequestedAsync(InvoiceDto invoice, CancellationToken ct)
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
            await _audit.LogAsync("ZATCA_SUBMIT_FAILED", nameof(Invoice), invoice.Id.ToString(), $"{invoice.InvoiceNumber}: {ex.Message}", ct);
        }
        return await GetAsync(invoice.Id, ct);
    }

    public async Task<InvoiceDto> CreateReturnAsync(CreateReturnInvoiceRequestDto r, CancellationToken ct = default)
        => await SendToZatcaIfRequestedAsync(await _tx.RunAsync(async token =>
        {
            var original = await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, token) ?? throw new NotFoundException(Messages.OriginalInvoiceNotFound);
            if (original.Status != "posted" || original.IsReturn) throw new ConflictException(Messages.OnlyPostedOriginalInvoiceCanBeReturned);
            if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException(Messages.ReturnReasonRequired);
            foreach (var ext in _extensions)
                if (await ext.CreateReturnAsync(r, original, CreateAsync, token) is { } extensionReturn) return extensionReturn;

            var previouslyReturned = (await _db.Set<InvoiceItem>().AsNoTracking()
                    .Where(i => _db.Set<Invoice>().Any(inv => inv.Id == i.InvoiceId && inv.OriginalInvoiceId == original.Id && inv.Status == "posted"))
                    .ToListAsync(token))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var lines = r.Lines.Count > 0
                ? r.Lines
                : original.Items.Select(i => new ReturnLineDto { ItemId = i.ItemId, Quantity = i.Quantity - previouslyReturned.GetValueOrDefault(i.ItemId) }).Where(l => l.Quantity > 0).ToList();
            if (lines.Count == 0) throw new ConflictException(Messages.NoRemainingQtyToReturn);

            var dto = new CreateInvoiceDto
            {
                Kind = original.Kind == InvoiceKind.Sales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn,
                InvoiceType = original.Kind == InvoiceKind.Sales ? InvoiceType.CreditNote : InvoiceType.DebitNote,
                IsReturn = true, OriginalInvoiceId = original.Id, OriginalInvoiceNumber = original.InvoiceNumber, ReturnReason = r.ReturnReason,
                PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber, PartyIdType = original.PartyIdType, PartyIdNumber = original.PartyIdNumber,
                PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
                WarehouseId = original.WarehouseId, // المرتجع يعود إلى مستودع فاتورته
                PartyStreet = original.PartyStreet, PartyBuildingNo = original.PartyBuildingNo, PartyDistrict = original.PartyDistrict,
                PartyCity = original.PartyCity, PartyPostalCode = original.PartyPostalCode, PartyAdditionalNo = original.PartyAdditionalNo, PartyCountry = original.PartyCountry,
                PaymentMethod = r.RefundPaymentMethod ?? original.PaymentMethod, PaymentMethodCode = r.RefundPaymentMethod == null ? original.PaymentMethodCode : r.RefundPaymentMethodCode, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,
                Notes = ReturnNotes(original, r),
                Status = "posted",
            };
            if (r.ReturnDate.HasValue) dto.IssueDate = r.ReturnDate.Value;
            foreach (var l in lines)
            {
                var src = original.Items.FirstOrDefault(i => i.ItemId == l.ItemId) ?? throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
                var remaining = src.Quantity - previouslyReturned.GetValueOrDefault(l.ItemId);
                if (l.Quantity <= 0 || l.Quantity > remaining)
                    throw new ValidationFailedException(string.Format(Messages.ReturnQtyRange, src.ItemName, remaining));
                dto.Items.Add(new InvoiceItemDto
                {
                    ItemId = src.ItemId, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit, Quantity = l.Quantity,
                    UnitPrice = src.UnitPrice, UnitCost = src.UnitCost, VatRate = src.VatRate,
                    VatCategory = src.VatCategory, VatExemptionReasonCode = src.VatExemptionReasonCode, RevenueAccountCode = src.RevenueAccountCode,
                    Discount = src.Quantity == 0 ? 0 : Math.Round(src.Discount * l.Quantity / src.Quantity, 2),
                    CostCenterId = src.CostCenterId,
                });
            }
            // خصم الفاتورة يُوزَّع نسبياً على الكميات المرتجعة.
            var origGross = original.Items.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
            var retGross = dto.Items.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
            dto.InvoiceDiscount = origGross == 0 ? 0 : DocumentPricing.Round(original.InvoiceDiscount * retGross / origGross);

            if (r.RefundPaymentMethod == null) MirrorOriginalSplits(dto, original);
            return await CreateAsync(dto, token);
        }, ct), ct);

    internal static string ReturnNotes(Invoice original, CreateReturnInvoiceRequestDto r)
        => string.IsNullOrWhiteSpace(r.Notes) ? $"مرتجع من الفاتورة {original.InvoiceNumber}" : $"مرتجع من الفاتورة {original.InvoiceNumber}: {r.Notes.Trim()}";

    /// <summary>بلا طريقة ردّ محددة: فاتورة دُفعت بأكثر من طريقة يُردّ مرتجعها بالتوزيع نفسه (بنسبة قيمة المرتجع).</summary>
    internal static void MirrorOriginalSplits(CreateInvoiceDto dto, Invoice original)
    {
        if (original.PaymentSplits.Count == 0 || original.GrandTotal <= 0) return;
        var returned = DocumentPricing.Price(
            dto.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride)).ToList(), dto.InvoiceDiscount).GrandTotal;
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
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).Include(i => i.VehicleBatches).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            // المرحّلة (قبل إرسالها للهيئة) تُحذف: يُعكس قيدها وحركاتها وتعود سياراتها؛ المسودة تُحذف مباشرة
            var wasPosted = invoice.Status == "posted";
            if (wasPosted) await UnpostForEditAsync(invoice, token, forDelete: true);
            else EnsureNotLinkedToAnotherDocument(invoice);
            await ReleaseSourceAsync(invoice, token);
            foreach (var ext in _extensions) await ext.OnSavingAsync(invoice, deleting: true, token);
            await _audit.LogAsync(wasPosted ? "INVOICE_POSTED_DELETED" : "INVOICE_DRAFT_DELETED", nameof(Invoice), invoice.Id.ToString(), $"حذف {(wasPosted ? $"{invoice.InvoiceNumber} المرحّلة" : "مسودة")} {DescribeKind(invoice.Kind)} بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            _db.RemoveRange(invoice.Items);
            _db.RemoveRange(invoice.PaymentSplits);
            _db.RemoveRange(invoice.VehicleLines);
            _db.RemoveRange(invoice.VehicleBatches);
            _db.Remove(invoice);
            await _db.SaveChangesAsync(token);
        }, ct);

    private static void EnsureNotLinkedToAnotherDocument(Invoice invoice)
    {
        if (invoice.ReferenceType != null || invoice.ReferenceId.HasValue || invoice.OriginalInvoiceId.HasValue)
            throw new ConflictException(Messages.InvoiceFromSourceDocument);
    }

    /// <summary>عند حذف فاتورة محوّلة من مستند تجاري يُفَك الارتباط ليعود المستند قابلاً للتحويل.</summary>
    private async Task ReleaseSourceAsync(Invoice invoice, CancellationToken ct)
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

    public Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default) => _zatca.SubmitInvoiceAsync(id, ct);

    // ---------------- القيد المحاسبي للفاتورة ----------------
    /// <summary>
    /// يُنفَّذ البناء والترحيل الحقيقيان داخل معاملة تُلغى دائماً، فتطابق المعاينة الترحيل الفعلي حرفياً
    /// (تكلفة المخزون بالمتوسط، تكلفة المركبات، حسابات الدفع والطرف، الحد الائتماني) دون أي أثر محفوظ أو رقم مستهلك.
    /// </summary>
    public async Task<InvoiceJournalDto> PreviewJournalAsync(CreateInvoiceDto r, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction != null) throw new InvalidOperationException(Messages.JournalPreviewInsideTransaction);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = await BuildAsync(r, ct);
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

    public async Task<InvoiceJournalDto> GetJournalAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await _db.Set<Invoice>().AsNoTracking().Include(i => i.VehicleLines).Include(i => i.VehicleBatches).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(Messages.InvoiceNotFound);
        foreach (var ext in _extensions) await ext.EnsureJournalVisibleAsync(invoice, ct);
        if (invoice.JournalEntryId == null) throw new ConflictException(Messages.InvoiceNotPostedUsePreview);
        return await JournalOfAsync(invoice, ct);
    }

    private async Task<InvoiceJournalDto> JournalOfAsync(Invoice invoice, CancellationToken ct)
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
    /// التصنيف الضريبي يُعرَّف على الصنف: السطر المرتبط بصنف صفري أو معفى (بسببه) ولم يُحدَّد تصنيفه صراحةً يرثهما منه بنسبة صفر.
    /// الصنف الخاضع أو القديم بلا تعريف يبقى على سلوكه السابق (نسبة السطر).
    /// </summary>
    private async Task ApplyProductVatDefaultsAsync(CreateInvoiceDto r, CancellationToken ct)
    {
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

    // ---------------- البناء والتحقق (بدون أي أثر جانبي) ----------------
    private async Task<Invoice> BuildAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.ItemsDerivedFromVehicles) r.Items.Clear(); // إعادة تنفيذ بعد تعارض تزامن: البنود تُشتق من جديد
        // أسطر الوحدات (سيارات المعارض...) تشتق r.Items وتتحقق منها
        IInvoiceExtension? owner = null;
        Func<Invoice, CancellationToken, Task>? attachLines = null;
        foreach (var ext in _extensions)
            if ((attachLines = await ext.ResolveAsync(r, ct)) != null) { owner = ext; break; }
        r.ItemsDerivedFromVehicles = attachLines != null;
        await ApplyProductVatDefaultsAsync(r, ct);
        var errors = new List<string>();
        if (!Enum.IsDefined(r.Kind)) errors.Add(Messages.InvalidDocumentType);
        if (!Enum.IsDefined(r.InvoiceType)) errors.Add(Messages.InvalidInvoiceType);
        if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneItemRequired);
        if (r.InvoiceDiscount < 0) errors.Add(Messages.InvoiceDiscountCannotBeNegative);
        if (r.ExchangeRate <= 0) errors.Add(Messages.ExchangeRateMustBePositive);
        // التصنيف الضريبي غير الأساسي لا يحمل ضريبة
        if (r.Items.Any(i => i.VatCategory is VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.OutOfScope && (i.VatRate > 0 || i.VatAmountOverride > 0)))
            errors.Add(Messages.VatCategoryRateMismatch);
        ValidateExemptionReasons(r, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var isReturn = r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn;
        if (isReturn && r.OriginalInvoiceId == null) throw new ValidationFailedException(Messages.ReturnNeedsOriginalInvoice);
        if (isSales && r.Kind == InvoiceKind.Sales && r.InvoiceType is InvoiceType.CreditNote or InvoiceType.DebitNote)
            throw new ValidationFailedException(Messages.RegularInvoiceTypeInvalid);

        var priced = DocumentPricing.Price(
            r.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride)).ToList(), r.InvoiceDiscount);

        var productIds = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        if (r.Items.Any(i => i.ItemId == Guid.Empty && string.IsNullOrWhiteSpace(i.ItemName)))
            throw new ValidationFailedException(Messages.ServiceLineNeedsDescription);
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new ValidationFailedException(Messages.ItemsNotFoundInInvoice);

        var invoice = Mapper.Map<Invoice>(r);
        invoice.Id = Guid.NewGuid();
        invoice.Uuid = Guid.NewGuid();
        invoice.Status = "draft";
        invoice.IsReturn = isReturn;
        var module = owner?.Module;
        if (module == null && r.ReferenceId.HasValue)
            foreach (var ext in _extensions)
                if (await ext.OwnsReferenceAsync(r.ReferenceType, r.ReferenceId.Value, ct)) { module = ext.Module; break; }
        invoice.Module = module ?? PlatformModules.Accounting;
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
        invoice.VatTotal = priced.VatTotal;
        invoice.GrandTotal = priced.GrandTotal;
        invoice.ZatcaStatus = ZatcaSubmissionStatus.NotSubmitted;
        // خيار الشاشة «إرسال للهيئة»؛ إن لم يُرسل فبحسب إعداد المنشأة (الإرسال التلقائي عند الحفظ)
        invoice.SendToZatca = isSales && (r.SendToZatca
            ?? await _db.Set<Tenant>().AsNoTracking().Select(t => t.ZatcaConfig.AutoSubmitOnInvoiceSave).FirstAsync(ct));
        invoice.ZatcaQrCode = null; invoice.ZatcaHash = null; invoice.ZatcaUblXml = null; invoice.ZatcaPih = null;
        invoice.JournalEntryId = null;

        // الأسطر: الأرقام من السرفر دائماً، وبيانات الصنف من الكتالوج إن لم تُرسل.
        invoice.Items.Clear();
        for (var i = 0; i < r.Items.Count; i++)
        {
            var src = r.Items[i]; var pl = priced.Lines[i];
            products.TryGetValue(src.ItemId, out var p); // بند الخدمة: بلا صنف
            invoice.Items.Add(new InvoiceItem
            {
                ItemId = src.ItemId,
                ItemName = string.IsNullOrWhiteSpace(src.ItemName) ? p!.NameAr : src.ItemName,
                Sku = string.IsNullOrWhiteSpace(src.Sku) ? p?.Sku ?? string.Empty : src.Sku,
                Unit = string.IsNullOrWhiteSpace(src.Unit) ? p?.Unit ?? string.Empty : src.Unit,
                Quantity = src.Quantity, UnitPrice = src.UnitPrice, Discount = src.Discount,
                UnitCost = src.ItemId == Guid.Empty ? src.UnitCost : 0, // بند الخدمة يحمل تكلفته (مثل تكلفة السيارة) لأن لا مخزون يحسبها
                VatAmountOverride = src.VatAmountOverride,
                VatCategory = src.VatCategory ?? (src.VatRate > 0 || src.VatAmountOverride > 0 ? VatCategory.Standard : VatCategory.ZeroRated),
                VatExemptionReasonCode = VatExemptionReasons.Find(src.VatExemptionReasonCode)?.Code,
                VatRate = src.VatRate, VatAmount = pl.VatAmount, TotalBeforeVat = pl.Net, TotalAfterVat = pl.Total,
                CostCenterId = src.CostCenterId,
                RevenueAccountCode = isSales && !string.IsNullOrWhiteSpace(src.RevenueAccountCode) ? src.RevenueAccountCode.Trim() : null,
            });
        }

        if (attachLines != null) await attachLines(invoice, ct);

        // الطرف
        if (r.PartyId.HasValue)
        {
            if (isSales)
            {
                var c = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.CustomerNotFound);
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
        if (r.Kind == InvoiceKind.Sales && r.InvoiceType == InvoiceType.TaxInvoice) ValidateTaxInvoiceBuyer(invoice, domesticParty);

        // الدفع
        invoice.PaymentSplits.Clear();
        if (r.IsSplitPayment)
        {
            if (r.PaymentSplits.Count == 0 || r.PaymentSplits.Any(s => s.Amount <= 0))
                throw new ValidationFailedException(Messages.SplitPaymentsNeedPositiveAmounts);
            if (Math.Abs(r.PaymentSplits.Sum(s => s.Amount) - invoice.GrandTotal) > 0.005m)
                throw new ValidationFailedException(Messages.SplitPaymentsMustEqualInvoiceTotal);
            foreach (var s in r.PaymentSplits)
                invoice.PaymentSplits.Add(new InvoicePaymentSplit { Method = s.Method, PaymentMethodCode = s.PaymentMethodCode, Amount = s.Amount, Reference = s.Reference });
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

        // المرتجع: تحقّق من الفاتورة الأصلية
        if (isReturn)
        {
            var orig = await _db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, ct)
                ?? throw new ValidationFailedException(Messages.OriginalInvoiceNotFoundDot);
            var expected = r.Kind == InvoiceKind.SalesReturn ? InvoiceKind.Sales : InvoiceKind.Purchase;
            if (orig.Kind != expected || orig.Status != "posted") throw new ConflictException(Messages.OriginalInvoiceNotReturnable);
            invoice.OriginalInvoiceNumber = orig.InvoiceNumber;
            invoice.Module = orig.Module;
        }
        return invoice;
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
    private static void ValidateTaxInvoiceBuyer(Invoice invoice, bool domestic)
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
    private async Task FinalizeAsync(Invoice invoice, CancellationToken ct, bool preview = false)
    {
        if (invoice.InvoiceNumber.StartsWith(DraftPrefix, StringComparison.Ordinal))
            invoice.InvoiceNumber = invoice.ReferenceType == "pos_transaction" && invoice.Kind == InvoiceKind.Sales
                ? await _numbers.NextAsync("pos_invoice", "POS-", ct) // ترقيم نقاط البيع مستقل ومتسلسل
                : await _numbers.NextAsync(KeyFor(invoice.Kind), PrefixFor(invoice.Kind), ct);
        if (!preview) foreach (var ext in _extensions) await ext.OnPostingAsync(invoice, ct); // سجلات الوحدات (إنشاء المركبات وبيعها...)
        var isSales = invoice.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);

        // الدفاتر والمخزون بالعملة الأساسية: مبالغ المستند تُحوَّل بسعر صرفه (1 للعملة الأساسية)
        var fx = invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate;
        var baseTotal = DocumentPricing.Round(invoice.GrandTotal * fx);
        var baseVat = DocumentPricing.Round(invoice.VatTotal * fx);
        var baseNet = baseTotal - baseVat;
        var payments = ToBase(await ResolvePaymentsAsync(invoice, ct), fx, invoice.GrandTotal, baseTotal);
        decimal totalCost = 0;

        Dictionary<Guid, decimal> originalCosts = new();
        if (invoice.Kind == InvoiceKind.SalesReturn && invoice.OriginalInvoiceId.HasValue)
            originalCosts = (await _db.Set<InvoiceItem>().AsNoTracking().Where(i => i.InvoiceId == invoice.OriginalInvoiceId).ToListAsync(ct))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First().UnitCost);

        foreach (var item in invoice.Items.Where(i => i.ItemId != Guid.Empty))
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
            totalCost += invoice.Kind is InvoiceKind.Purchase or InvoiceKind.SalesReturn ? recorded.ValueChange : -recorded.ValueChange;
        }
        var stockCost = totalCost; // قيمة ما دخل/خرج من المخزون فعلاً (الأصناف المخزنية فقط)
        foreach (var svc in invoice.Items.Where(i => i.ItemId == Guid.Empty && i.UnitCost > 0))
            totalCost += Math.Round(svc.Quantity * svc.UnitCost, 2);
        invoice.TotalCost = totalCost; // بالعملة الأساسية
        invoice.GrossProfit = isSales ? (invoice.IsReturn ? -(baseNet - totalCost) : baseNet - totalCost) : 0;

        // مشتريات: البنود غير المخزنية (خدمات/مصروفات) لا تدخل حساب المخزون، إلا إن حدّد المستند حساب مخزون صراحةً
        // (شراء السيارات: بنودها وصفية وهي مخزون فعلاً).
        var expenseAmount = isSales || !string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
            : Math.Min(baseNet, DocumentPricing.Round(invoice.Items.Where(i => i.ItemId == Guid.Empty).Sum(i => i.TotalBeforeVat) * fx));
        if (expenseAmount > 0) await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.PurchasedServices);
        // المخزون بتغيّر قيمته الدفترية لا بسعر المستند: مرتجع المشتريات يخرج بتكلفته الحالية، والشراء بالمعيارية يدخل بها،
        // وما زاد أو نقص عن صافي المستند فرق سعر. البنود غير المخزنية على حساب مخزون صريح تدخله بصافيها.
        decimal? inventoryAmount = null;
        string? varianceAccount = null;
        if (!isSales && invoice.VehicleLines.Count == 0 && invoice.Items.Any(i => i.ItemId != Guid.Empty))
        {
            var nonStockNet = string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
                : DocumentPricing.Round(invoice.Items.Where(i => i.ItemId == Guid.Empty).Sum(i => i.TotalBeforeVat) * fx);
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
                NetAmount = baseNet, VatAmount = baseVat, CostAmount = totalCost, Payments = payments,
                RevenueAccountCode = invoice.RevenueAccountCode, InventoryAccountCode = invoice.InventoryAccountCode, CogsAccountCode = invoice.CogsAccountCode,
                RevenueLines = RevenueByLine(invoice, fx, baseNet),
            }, ct);
        else
            posted = await _posting.PostPurchaseAsync(new PurchasePostingRequest
            {
                Module = invoice.Module, Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id, ReplaceEntryId = invoice.JournalEntryId,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, VatAmount = baseVat, Payments = payments,
                ExpenseAmount = expenseAmount, InventoryAmount = inventoryAmount, VarianceAccountCode = varianceAccount,
                InventoryAccountCode = invoice.InventoryAccountCode,
                CostCenterId = invoice.Items.Select(i => i.CostCenterId).FirstOrDefault(c => c.HasValue),
            }, ct);
        invoice.JournalEntryId = posted.JournalEntryId;

        if (isSales && !preview)
            invoice.ZatcaQrCode = _zatca.GenerateTlvQr(tenant.NameAr, tenant.VatNumber,
                invoice.IssueDate.Date.Add(TimeSpan.TryParse(invoice.IssueTime, out var t) ? t : TimeSpan.Zero), invoice.GrandTotal, invoice.VatTotal);

        invoice.Status = "posted";
        await _db.SaveChangesAsync(ct);
        if (!preview)
            await _audit.LogAsync("INVOICE_POSTED", nameof(Invoice), invoice.Id.ToString(),
                $"ترحيل {DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} بإجمالي {invoice.GrandTotal:0.00} {invoice.CurrencyCode} - {invoice.PartyName}", ct);
    }

    private async Task<List<PaymentPosting>> ResolvePaymentsAsync(Invoice invoice, CancellationToken ct)
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

    /// <summary>إيراد كل سطر على حسابه ومركز تكلفته بالعملة الأساسية؛ كسر التقريب على آخر سطر ليطابق صافي المستند.</summary>
    private static List<RevenuePosting> RevenueByLine(Invoice invoice, decimal fx, decimal baseNet)
    {
        var lines = invoice.Items.Select(i => new RevenuePosting(i.RevenueAccountCode, i.CostCenterId, DocumentPricing.Round(i.TotalBeforeVat * fx))).ToList();
        if (lines.Count > 0) lines[^1] = lines[^1] with { Amount = baseNet - lines.Take(lines.Count - 1).Sum(l => l.Amount) };
        return lines;
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

    private async Task<string?> PartyAccountAsync(Invoice invoice, bool isSales, CancellationToken ct)
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
