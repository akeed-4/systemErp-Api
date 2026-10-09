using System.Text;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
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
    private readonly IVehicleService _vehicles;
    private readonly ERP.Core.Contracts.Shared.IAuditService _audit;

    public InvoiceService(ErpDbContext db, IAccountingPostingService posting, IInventoryService inventory,
        INumberSequenceService numbers, IZatcaService zatca, ITransactionRunner tx, IVehicleService vehicles, ERP.Core.Contracts.Shared.IAuditService audit)
    {
        _audit = audit;
        _db = db; _posting = posting; _inventory = inventory; _numbers = numbers; _zatca = zatca; _tx = tx; _vehicles = vehicles;
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
        var items = await q.Include(i => i.Items).Include(i => i.PaymentSplits)
            .OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).AsSplitQuery().ToListAsync(ct);
        var due = await InvoiceBalances.ForAsync(_db, items.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, ct);
        return new PagedResult<InvoiceDto>
        {
            Items = items.Select(i => ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0)).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL، والمتبقي يُحسب لفواتير الصفحة فقط.</summary>
    public Task<LoadResult> LoadAsync(InvoiceKind? kind, DataSourceLoadOptions options, bool? hasVehicleLines = null, CancellationToken ct = default)
    {
        var q = _db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (hasVehicleLines.HasValue) q = hasVehicleLines.Value ? q.Where(i => i.VehicleLines.Any()) : q.Where(i => !i.VehicleLines.Any());
        return EntityLoader.LoadAsync(q.Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery(), options, async (page, token) =>
        {
            var due = await InvoiceBalances.ForAsync(_db, page.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, token);
            return page.Select(i => ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0)).ToList();
        }, ct, EntityLoader.Desc(nameof(Invoice.IssueDate)), EntityLoader.Desc(nameof(Invoice.InvoiceNumber)));
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var invoice = await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(Messages.InvoiceNotFound);
        return ToDto(invoice, invoice.Status == "posted" ? await InvoiceBalances.DueAsync(_db, id, ct) : 0);
    }

    private static InvoiceDto ToDto(Invoice invoice, decimal amountDue)
    {
        var dto = Mapper.Map<InvoiceDto>(invoice);
        dto.AmountDue = amountDue;
        return dto;
    }

    // ---------------- الإنشاء ----------------
    public Task<InvoiceDto> CreateAsync(CreateInvoiceDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await BuildAsync(r, token);
            var draft = string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
            invoice.Status = "draft";
            // المسودة برقم مؤقت؛ الرقم المتسلسل يُصرف عند الترحيل فلا يترك حذف مسودة فجوة في تسلسل الفواتير
            invoice.InvoiceNumber = DraftPrefix + invoice.Id.ToString("N")[..10].ToUpperInvariant();
            _db.Add(invoice);
            await _db.SaveChangesAsync(token);
            if (!draft) await FinalizeAsync(invoice, token);
            return await GetAsync(invoice.Id, token);
        }, ct);

    public Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            EnsureNotLinkedToAnotherDocument(invoice);
            if (r.Kind != invoice.Kind) throw new ConflictException(Messages.CannotChangeDocumentType);
            // الفاتورة المرحّلة مستند صادر لا يُعدَّل: تُصحَّح بمرتجع (إشعار دائن/مدين) وفاتورة جديدة
            if (invoice.Status == "posted") throw new ConflictException(Messages.PostedInvoiceLocked);
            if (invoice.Status != "draft") throw new ConflictException(Messages.CannotEditCancelledInvoice);

            var fresh = await BuildAsync(r, token); // نفس تحقق الإنشاء وحساب الأرقام في السرفر
            var (keepId, keepTenant, keepCreated, keepNumber, keepUuid) = (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid);

            _db.RemoveRange(invoice.Items);
            _db.RemoveRange(invoice.PaymentSplits);
            _db.RemoveRange(invoice.VehicleLines);
            invoice.Items.Clear();
            invoice.PaymentSplits.Clear();
            invoice.VehicleLines.Clear();
            Mapper.Apply(fresh, invoice);
            (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid) = (keepId, keepTenant, keepCreated, keepNumber, keepUuid);
            invoice.Status = "draft";
            foreach (var item in fresh.Items) invoice.Items.Add(item);
            foreach (var split in fresh.PaymentSplits) invoice.PaymentSplits.Add(split);
            foreach (var vl in fresh.VehicleLines) invoice.VehicleLines.Add(vl);
            await _db.SaveChangesAsync(token);

            if (!string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase)) await FinalizeAsync(invoice, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (invoice.Status != "draft") throw new ConflictException(Messages.InvoiceAlreadyPostedOrCancelled);
            // سير الموافقات: المسودة المعلّقة أو المرفوضة لا تُرحَّل (آخر طلب على المستند هو المرجع)
            var approval = await _db.Set<ApprovalRequest>().Where(r => r.DocumentId == id)
                .OrderByDescending(r => r.CreatedAt).Select(r => r.Status).FirstOrDefaultAsync(token);
            if (approval == "pending") throw new ConflictException(Messages.InvoicePendingApproval);
            if (approval == "rejected") throw new ConflictException(Messages.InvoiceApprovalRejected);
            await FinalizeAsync(invoice, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<InvoiceDto> CreateReturnAsync(CreateReturnInvoiceRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var original = await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, token) ?? throw new NotFoundException(Messages.OriginalInvoiceNotFound);
            if (original.Status != "posted" || original.IsReturn) throw new ConflictException(Messages.OnlyPostedOriginalInvoiceCanBeReturned);
            if (await _db.Set<InvoiceVehicleLine>().AnyAsync(l => l.InvoiceId == original.Id, token))
                throw new ConflictException(Messages.MultiVehicleReturnNotSupported);
            if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException(Messages.ReturnReasonRequired);

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
                PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber,
                PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
                WarehouseId = original.WarehouseId, // المرتجع يعود إلى مستودع فاتورته
                PartyStreet = original.PartyStreet, PartyBuildingNo = original.PartyBuildingNo, PartyDistrict = original.PartyDistrict,
                PartyCity = original.PartyCity, PartyPostalCode = original.PartyPostalCode, PartyAdditionalNo = original.PartyAdditionalNo, PartyCountry = original.PartyCountry,
                PaymentMethod = r.RefundPaymentMethod ?? original.PaymentMethod, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,
                Notes = $"مرتجع من الفاتورة {original.InvoiceNumber}",
                Status = "posted",
            };
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
        }, ct);

    /// <summary>بلا طريقة ردّ محددة: فاتورة دُفعت بأكثر من طريقة يُردّ مرتجعها بالتوزيع نفسه (بنسبة قيمة المرتجع).</summary>
    private static void MirrorOriginalSplits(CreateInvoiceDto dto, Invoice original)
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
            if (amount > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = split.Method, Amount = amount, Reference = split.Reference });
        }
        dto.IsSplitPayment = dto.PaymentSplits.Count > 0;
    }

    /// <summary>حذف مسودة فقط. الفاتورة المرحّلة مستند صادر لا يُحذف: تُلغى بمرتجع كامل (إشعار دائن/مدين).</summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).Include(i => i.VehicleLines).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (invoice.Status == "posted") throw new ConflictException(Messages.PostedInvoiceLocked);
            EnsureNotLinkedToAnotherDocument(invoice);
            await ReleaseSourceAsync(invoice, token);
            await _audit.LogAsync("INVOICE_DRAFT_DELETED", nameof(Invoice), invoice.Id.ToString(), $"حذف مسودة {DescribeKind(invoice.Kind)} بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            _db.RemoveRange(invoice.Items);
            _db.RemoveRange(invoice.PaymentSplits);
            _db.RemoveRange(invoice.VehicleLines);
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
        var invoice = await _db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(Messages.InvoiceNotFound);
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
        var vehicleLines = r.VehicleLines.Count > 0 ? await VehicleInvoiceLines.ResolveAsync(_db, r, ct) : null; // يشتق r.Items ويتحقق من الأسطر
        r.ItemsDerivedFromVehicles = vehicleLines != null;
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

        invoice.VehicleLines.Clear();
        if (vehicleLines != null) foreach (var vl in vehicleLines) invoice.VehicleLines.Add(vl);
        if (vehicleLines != null) // حسابات السيارات الافتراضية عند عدم تحديدها من العميل
        {
            if (string.IsNullOrWhiteSpace(invoice.InventoryAccountCode)) invoice.InventoryAccountCode = DefaultAccounts.VehicleInventory;
            if (isSales && string.IsNullOrWhiteSpace(invoice.RevenueAccountCode)) invoice.RevenueAccountCode = DefaultAccounts.CarSalesRevenue;
            if (isSales && string.IsNullOrWhiteSpace(invoice.CogsAccountCode)) invoice.CogsAccountCode = DefaultAccounts.CarCogs;
        }

        // الطرف
        if (r.PartyId.HasValue)
        {
            if (isSales)
            {
                var c = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.CustomerNotFound);
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = c.NameAr;
                invoice.PartyVatNumber ??= c.VatNumber; invoice.PartyCrNumber ??= c.CrNumber;
                invoice.PartyPhone ??= c.Phone; invoice.PartyEmail ??= c.Email;
                // العنوان الوطني من بطاقة العميل ما لم يُرسل مع الفاتورة
                invoice.PartyStreet ??= c.Street; invoice.PartyBuildingNo ??= c.BuildingNo; invoice.PartyDistrict ??= c.District;
                invoice.PartyCity ??= c.City; invoice.PartyPostalCode ??= c.PostalCode; invoice.PartyAdditionalNo ??= c.AdditionalNo;
            }
            else
            {
                var s = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.SupplierNotFound);
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = s.NameAr;
                invoice.PartyVatNumber ??= s.VatNumber; invoice.PartyCrNumber ??= s.CrNumber;
                invoice.PartyPhone ??= s.Phone; invoice.PartyEmail ??= s.Email;
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
                invoice.PaymentSplits.Add(new InvoicePaymentSplit { Method = s.Method, Amount = s.Amount, Reference = s.Reference });
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
        if (!preview) await VehicleInvoiceLines.OnPostingAsync(_db, _vehicles, invoice, ct); // شراء: إنشاء المركبات، بيع: تحويلها Sold
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
            totalCost += Math.Round(item.Quantity * recorded.UnitCost, 2);
        }
        var stockCost = totalCost; // تكلفة الأصناف المخزنية فقط (قيمة ما دخل/خرج من المخزون)
        foreach (var svc in invoice.Items.Where(i => i.ItemId == Guid.Empty && i.UnitCost > 0))
            totalCost += Math.Round(svc.Quantity * svc.UnitCost, 2);
        invoice.TotalCost = totalCost; // بالعملة الأساسية
        invoice.GrossProfit = isSales ? (invoice.IsReturn ? -(baseNet - totalCost) : baseNet - totalCost) : 0;

        // مشتريات: البنود غير المخزنية (خدمات/مصروفات) لا تدخل حساب المخزون، إلا إن حدّد المستند حساب مخزون صراحةً
        // (شراء السيارات: بنودها وصفية وهي مخزون فعلاً).
        var expenseAmount = isSales || !string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
            : Math.Min(baseNet, DocumentPricing.Round(invoice.Items.Where(i => i.ItemId == Guid.Empty).Sum(i => i.TotalBeforeVat) * fx));
        if (expenseAmount > 0) await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.PurchasedServices);
        // مرتجع المشتريات يخرج من المخزون بتكلفته الحالية لا بسعر الشراء: الفرق إلى حساب فروق أسعار المشتريات
        decimal? inventoryAmount = invoice.Kind == InvoiceKind.PurchaseReturn && invoice.VehicleLines.Count == 0 ? stockCost : null;
        if (inventoryAmount.HasValue && inventoryAmount != baseNet - expenseAmount)
            await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.PurchasePriceVariance);

        // القيد المحاسبي عبر المحرك المركزي
        var partyAccount = await PartyAccountAsync(invoice, isSales, ct);
        var description = $"{DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} - {invoice.PartyName}";
        PostingResult posted;
        if (isSales)
            posted = await _posting.PostSaleAsync(new SalePostingRequest
            {
                Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, VatAmount = baseVat, CostAmount = totalCost, Payments = payments,
                RevenueAccountCode = invoice.RevenueAccountCode, InventoryAccountCode = invoice.InventoryAccountCode, CogsAccountCode = invoice.CogsAccountCode,
                RevenueLines = RevenueByLine(invoice, fx, baseNet),
            }, ct);
        else
            posted = await _posting.PostPurchaseAsync(new PurchasePostingRequest
            {
                Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, VatAmount = baseVat, Payments = payments,
                ExpenseAmount = expenseAmount, InventoryAmount = inventoryAmount,
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
        if (invoice.PaymentSplits.Count > 0)
            return invoice.PaymentSplits.Where(s => s.Method != PaymentMethod.Credit)
                .Select(s => new PaymentPosting(TreasuryResolver.Resolve(s.Method, methods), s.Amount)).ToList();
        if (invoice.PaymentMethod == PaymentMethod.Credit || invoice.GrandTotal <= 0) return new();
        return new() { new PaymentPosting(TreasuryResolver.Resolve(invoice.PaymentMethod, methods), invoice.GrandTotal) };
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
