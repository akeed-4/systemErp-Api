using System.Text;
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

    public InvoiceService(ErpDbContext db, IAccountingPostingService posting, IInventoryService inventory,
        INumberSequenceService numbers, IZatcaService zatca, ITransactionRunner tx)
    {
        _db = db; _posting = posting; _inventory = inventory; _numbers = numbers; _zatca = zatca; _tx = tx;
    }

    // ---------------- القراءة ----------------
    public async Task<PagedResult<InvoiceDto>> ListAsync(InvoiceKind? kind, PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<Invoice>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
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
        return new PagedResult<InvoiceDto>
        {
            Items = items.Select(Mapper.Map<InvoiceDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<InvoiceDto>(await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("الفاتورة غير موجودة"));

    // ---------------- الإنشاء ----------------
    public Task<InvoiceDto> CreateAsync(CreateInvoiceDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await BuildAsync(r, token);
            var draft = string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
            invoice.Status = "draft";
            invoice.InvoiceNumber = invoice.ReferenceType == "pos_transaction" && invoice.Kind == InvoiceKind.Sales
                ? await _numbers.NextAsync("pos_invoice", "POS-", token) // ترقيم نقاط البيع مستقل ومتسلسل
                : await _numbers.NextAsync(KeyFor(invoice.Kind), PrefixFor(invoice.Kind), token);
            _db.Add(invoice);
            await _db.SaveChangesAsync(token);
            if (!draft) await FinalizeAsync(invoice, token);
            return await GetAsync(invoice.Id, token);
        }, ct);

    public Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException("الفاتورة غير موجودة");
            if (r.Kind != invoice.Kind) throw new ConflictException("لا يمكن تغيير نوع المستند.");
            // فاتورة مرحّلة: يُعكس أثرها المحاسبي والمخزني ثم تُعاد كمسودة وتُرحَّل من جديد بالقيم المعدّلة (نفس الرقم).
            if (invoice.Status == "posted") await UnpostAsync(invoice, bypassSourceGuard: false, token);
            else if (invoice.Status != "draft") throw new ConflictException("لا تُعدَّل فاتورة ملغاة.");

            var fresh = await BuildAsync(r, token); // نفس تحقق الإنشاء وحساب الأرقام في الخادم
            var (keepId, keepTenant, keepCreated, keepNumber, keepUuid) = (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid);

            _db.RemoveRange(invoice.Items);
            _db.RemoveRange(invoice.PaymentSplits);
            invoice.Items.Clear();
            invoice.PaymentSplits.Clear();
            Mapper.Apply(fresh, invoice);
            (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid) = (keepId, keepTenant, keepCreated, keepNumber, keepUuid);
            invoice.Status = "draft";
            foreach (var item in fresh.Items) invoice.Items.Add(item);
            foreach (var split in fresh.PaymentSplits) invoice.PaymentSplits.Add(split);
            await _db.SaveChangesAsync(token);

            if (!string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase)) await FinalizeAsync(invoice, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException("الفاتورة غير موجودة");
            if (invoice.Status != "draft") throw new ConflictException("الفاتورة مرحّلة مسبقاً أو ملغاة.");
            await FinalizeAsync(invoice, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<InvoiceDto> CreateReturnAsync(CreateReturnInvoiceRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var original = await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, token) ?? throw new NotFoundException("الفاتورة الأصلية غير موجودة");
            if (original.Status != "posted" || original.IsReturn) throw new ConflictException("يمكن إرجاع فاتورة مرحّلة أصلية فقط.");
            if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException("سبب الإرجاع مطلوب.");

            var previouslyReturned = (await _db.Set<InvoiceItem>().AsNoTracking()
                    .Where(i => _db.Set<Invoice>().Any(inv => inv.Id == i.InvoiceId && inv.OriginalInvoiceId == original.Id && inv.Status == "posted"))
                    .ToListAsync(token))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var lines = r.Lines.Count > 0
                ? r.Lines
                : original.Items.Select(i => new ReturnLineDto { ItemId = i.ItemId, Quantity = i.Quantity - previouslyReturned.GetValueOrDefault(i.ItemId) }).Where(l => l.Quantity > 0).ToList();
            if (lines.Count == 0) throw new ConflictException("لا توجد كميات متبقية للإرجاع.");

            var dto = new CreateInvoiceDto
            {
                Kind = original.Kind == InvoiceKind.Sales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn,
                InvoiceType = original.Kind == InvoiceKind.Sales ? InvoiceType.CreditNote : InvoiceType.DebitNote,
                IsReturn = true, OriginalInvoiceId = original.Id, OriginalInvoiceNumber = original.InvoiceNumber, ReturnReason = r.ReturnReason,
                PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber,
                PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
                PaymentMethod = r.RefundPaymentMethod ?? original.PaymentMethod, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,
                Notes = $"مرتجع من الفاتورة {original.InvoiceNumber}",
                Status = "posted",
            };
            foreach (var l in lines)
            {
                var src = original.Items.FirstOrDefault(i => i.ItemId == l.ItemId) ?? throw new ValidationFailedException("صنف غير موجود في الفاتورة الأصلية.");
                var remaining = src.Quantity - previouslyReturned.GetValueOrDefault(l.ItemId);
                if (l.Quantity <= 0 || l.Quantity > remaining)
                    throw new ValidationFailedException($"كمية الإرجاع للصنف {src.ItemName} يجب أن تكون بين 0 و{remaining:0.####}.");
                dto.Items.Add(new InvoiceItemDto
                {
                    ItemId = src.ItemId, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit, Quantity = l.Quantity,
                    UnitPrice = src.UnitPrice, UnitCost = src.UnitCost, VatRate = src.VatRate,
                    Discount = src.Quantity == 0 ? 0 : Math.Round(src.Discount * l.Quantity / src.Quantity, 2),
                    CostCenterId = src.CostCenterId,
                });
            }
            // خصم الفاتورة يُوزَّع نسبياً على الكميات المرتجعة.
            var origGross = original.Items.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
            var retGross = dto.Items.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
            dto.InvoiceDiscount = origGross == 0 ? 0 : DocumentPricing.Round(original.InvoiceDiscount * retGross / origGross);

            return await CreateAsync(dto, token);
        }, ct);

    /// <summary>حذف فاتورة: المسودة تُحذف مباشرة، والمرحّلة يُعكس أثرها (قيد + مخزون) ثم تُحذف مع بقاء قيد العكس للتدقيق.</summary>
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => DeleteCoreAsync(id, bypassSourceGuard: false, ct);

    /// <summary>حذف فاتورة أنشأتها وحدة أخرى (POS...) — تستدعيه الوحدة المصدر فقط بعد التحقق من قواعدها.</summary>
    public Task DeleteSourceInvoiceAsync(Guid id, CancellationToken ct = default) => DeleteCoreAsync(id, bypassSourceGuard: true, ct);

    private Task DeleteCoreAsync(Guid id, bool bypassSourceGuard, CancellationToken ct)
        => _tx.RunAsync(async token =>
        {
            var invoice = await _db.Set<Invoice>().Include(i => i.Items).Include(i => i.PaymentSplits).AsSplitQuery()
                .FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException("الفاتورة غير موجودة");
            if (invoice.Status == "posted") await UnpostAsync(invoice, bypassSourceGuard, token);
            await ReleaseSourceAsync(invoice, token);
            _db.RemoveRange(invoice.Items);
            _db.RemoveRange(invoice.PaymentSplits);
            _db.Remove(invoice);
            await _db.SaveChangesAsync(token);
        }, ct);

    private static readonly string[] OwnedByOtherModules = { "pos_transaction", "car_sales_contract", "car_procurement_order", "commercial_contract" };

    /// <summary>يعكس أثر فاتورة مرحّلة (قيد + حركات مخزون) ويعيدها مسودة. يرفض ما تعتمد عليه مستندات أخرى.</summary>
    private async Task UnpostAsync(Invoice invoice, bool bypassSourceGuard, CancellationToken ct)
    {
        if (invoice.ZatcaStatus is ZatcaSubmissionStatus.Cleared or ZatcaSubmissionStatus.Reported)
            throw new ConflictException("الفاتورة أُرسلت لهيئة الزكاة ولا يجوز تعديلها أو حذفها؛ أصدر إشعاراً دائناً.");
        if (!bypassSourceGuard && invoice.ReferenceType != null && OwnedByOtherModules.Contains(invoice.ReferenceType))
            throw new ConflictException("الفاتورة ناتجة عن مستند مصدر (نقطة بيع/عقد/أمر شراء)؛ عدّلها أو أعكسها من ذلك المستند.");
        if (await _db.Set<Invoice>().AnyAsync(i => i.OriginalInvoiceId == invoice.Id && i.Status == "posted", ct))
            throw new ConflictException("للفاتورة مرتجعات مرحّلة؛ احذفها أولاً.");

        if (invoice.JournalEntryId.HasValue)
            await _posting.ReverseAsync(invoice.JournalEntryId.Value, $"إلغاء ترحيل الفاتورة {invoice.InvoiceNumber}", ct);
        await _inventory.RemoveDocumentMovementsAsync("invoice", invoice.Id, ct);

        invoice.JournalEntryId = null; invoice.Status = "draft"; invoice.ZatcaQrCode = null;
        invoice.TotalCost = 0; invoice.GrossProfit = 0;
        foreach (var item in invoice.Items.Where(i => i.ItemId != Guid.Empty)) item.UnitCost = 0;
        await _db.SaveChangesAsync(ct);
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

    // ---------------- البناء والتحقق (بدون أي أثر جانبي) ----------------
    private async Task<Invoice> BuildAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(r.Kind)) errors.Add("نوع المستند غير صالح.");
        if (!Enum.IsDefined(r.InvoiceType)) errors.Add("نوع الفاتورة غير صالح.");
        if (r.Items.Count == 0) errors.Add("يجب إدخال صنف واحد على الأقل.");
        if (r.InvoiceDiscount < 0) errors.Add("خصم الفاتورة لا يكون سالباً.");
        if (r.ExchangeRate <= 0) errors.Add("سعر الصرف يجب أن يكون موجباً.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var isReturn = r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn;
        if (isReturn && r.OriginalInvoiceId == null) throw new ValidationFailedException("المرتجع يحتاج فاتورة أصلية.");
        if (isSales && r.Kind == InvoiceKind.Sales && r.InvoiceType is InvoiceType.CreditNote or InvoiceType.DebitNote)
            throw new ValidationFailedException("الفاتورة الاعتيادية ضريبية أو مبسطة؛ الإشعارات للمرتجعات.");

        var priced = DocumentPricing.Price(
            r.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride)).ToList(), r.InvoiceDiscount);

        var productIds = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        if (r.Items.Any(i => i.ItemId == Guid.Empty && string.IsNullOrWhiteSpace(i.ItemName)))
            throw new ValidationFailedException("بند الخدمة (بدون صنف) يحتاج وصفاً.");
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new ValidationFailedException("أصناف غير موجودة في الفاتورة.");

        var invoice = Mapper.Map<Invoice>(r);
        invoice.Id = Guid.NewGuid();
        invoice.Uuid = Guid.NewGuid();
        invoice.Status = "draft";
        invoice.IsReturn = isReturn;
        invoice.IssueDate = r.IssueDate == default ? DateTime.UtcNow : r.IssueDate;
        invoice.IssueTime = string.IsNullOrWhiteSpace(r.IssueTime) ? DateTime.UtcNow.ToString("HH:mm:ss") : r.IssueTime;
        invoice.CurrencyCode = string.IsNullOrWhiteSpace(r.CurrencyCode) ? "SAR" : r.CurrencyCode;
        invoice.Subtotal = priced.NetTotal;
        invoice.ItemsDiscountTotal = priced.ItemsDiscountTotal;
        invoice.InvoiceDiscount = priced.InvoiceDiscount;
        invoice.DiscountTotal = priced.DiscountTotal;
        invoice.VatTotal = priced.VatTotal;
        invoice.GrandTotal = priced.GrandTotal;
        invoice.ZatcaStatus = ZatcaSubmissionStatus.NotSubmitted;
        invoice.ZatcaQrCode = null; invoice.ZatcaHash = null; invoice.ZatcaUblXml = null; invoice.ZatcaPih = null;
        invoice.JournalEntryId = null;

        // الأسطر: الأرقام من الخادم دائماً، وبيانات الصنف من الكتالوج إن لم تُرسل.
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
                VatRate = src.VatRate, VatAmount = pl.VatAmount, TotalBeforeVat = pl.Net, TotalAfterVat = pl.Total,
                CostCenterId = src.CostCenterId,
            });
        }

        // الطرف
        if (r.PartyId.HasValue)
        {
            if (isSales)
            {
                var c = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException("العميل غير موجود.");
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = c.NameAr;
                invoice.PartyVatNumber ??= c.VatNumber; invoice.PartyCrNumber ??= c.CrNumber;
                invoice.PartyPhone ??= c.Phone; invoice.PartyEmail ??= c.Email;
            }
            else
            {
                var s = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException("المورد غير موجود.");
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = s.NameAr;
                invoice.PartyVatNumber ??= s.VatNumber; invoice.PartyCrNumber ??= s.CrNumber;
                invoice.PartyPhone ??= s.Phone; invoice.PartyEmail ??= s.Email;
            }
        }
        if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = isSales ? "عميل نقدي" : "مورد نقدي";
        if (!string.IsNullOrWhiteSpace(invoice.PartyVatNumber) && !SaudiVat.IsValid(invoice.PartyVatNumber))
            throw new ValidationFailedException("الرقم الضريبي للطرف غير صالح.");
        if (r.Kind == InvoiceKind.Sales && r.InvoiceType == InvoiceType.TaxInvoice && string.IsNullOrWhiteSpace(invoice.PartyVatNumber))
            throw new ValidationFailedException("الفاتورة الضريبية (B2B) تتطلب الرقم الضريبي للمشتري.");

        // الدفع
        invoice.PaymentSplits.Clear();
        if (r.IsSplitPayment)
        {
            if (r.PaymentSplits.Count == 0 || r.PaymentSplits.Any(s => s.Amount <= 0))
                throw new ValidationFailedException("الدفعات المقسّمة تحتاج مبالغ موجبة.");
            if (Math.Abs(r.PaymentSplits.Sum(s => s.Amount) - invoice.GrandTotal) > 0.005m)
                throw new ValidationFailedException("مجموع الدفعات المقسّمة يجب أن يساوي إجمالي الفاتورة.");
            foreach (var s in r.PaymentSplits)
                invoice.PaymentSplits.Add(new InvoicePaymentSplit { Method = s.Method, Amount = s.Amount, Reference = s.Reference });
        }
        var hasCredit = invoice.PaymentMethod == PaymentMethod.Credit || invoice.PaymentSplits.Any(s => s.Method == PaymentMethod.Credit);
        if (hasCredit && !r.PartyId.HasValue)
            throw new ValidationFailedException(isSales ? "البيع الآجل يتطلب تحديد العميل." : "الشراء الآجل يتطلب تحديد المورد.");

        if (hasCredit && r.Kind == InvoiceKind.Sales)
        {
            var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(x => x.Id == r.PartyId, ct);
            var creditPart = invoice.PaymentMethod == PaymentMethod.Credit && !r.IsSplitPayment
                ? invoice.GrandTotal : invoice.PaymentSplits.Where(s => s.Method == PaymentMethod.Credit).Sum(s => s.Amount);
            if (customer.CreditLimit > 0 && customer.CurrentBalance + creditPart > customer.CreditLimit)
                throw new ConflictException($"تجاوز الحد الائتماني للعميل ({customer.CreditLimit:0.00}). الرصيد الحالي {customer.CurrentBalance:0.00}.");
        }

        // المرتجع: تحقّق من الفاتورة الأصلية
        if (isReturn)
        {
            var orig = await _db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, ct)
                ?? throw new ValidationFailedException("الفاتورة الأصلية غير موجودة.");
            var expected = r.Kind == InvoiceKind.SalesReturn ? InvoiceKind.Sales : InvoiceKind.Purchase;
            if (orig.Kind != expected || orig.Status != "posted") throw new ConflictException("الفاتورة الأصلية غير صالحة للإرجاع.");
            invoice.OriginalInvoiceNumber = orig.InvoiceNumber;
        }
        return invoice;
    }

    // ---------------- الترحيل: مخزون + قيد + QR ----------------
    private async Task FinalizeAsync(Invoice invoice, CancellationToken ct)
    {
        var isSales = invoice.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        var payments = await ResolvePaymentsAsync(invoice, ct);
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
                InvoiceKind.Purchase => new RecordStockMovementDto { Type = StockMovementType.InPurchase, UnitCost = item.Quantity == 0 ? 0 : Math.Round(item.TotalBeforeVat / item.Quantity, 4) },
                InvoiceKind.SalesReturn => new RecordStockMovementDto { Type = StockMovementType.AdjustmentIn, UnitCost = originalCosts.GetValueOrDefault(item.ItemId) },
                _ => new RecordStockMovementDto { Type = StockMovementType.AdjustmentOut },
            };
            movement.ItemId = item.ItemId; movement.Quantity = item.Quantity;
            movement.UnitPrice = item.UnitPrice; movement.ReferenceNumber = invoice.InvoiceNumber;
            movement.Date = invoice.IssueDate;
            movement.SourceType = "invoice"; movement.SourceId = invoice.Id;

            var recorded = await _inventory.RecordMovementAsync(movement, ct);
            item.UnitCost = invoice.Kind == InvoiceKind.Purchase ? movement.UnitCost : recorded.UnitCost;
            totalCost += Math.Round(item.Quantity * recorded.UnitCost, 2);
        }
        foreach (var svc in invoice.Items.Where(i => i.ItemId == Guid.Empty && i.UnitCost > 0))
            totalCost += Math.Round(svc.Quantity * svc.UnitCost, 2);
        invoice.TotalCost = totalCost;
        invoice.GrossProfit = isSales ? (invoice.IsReturn ? -(invoice.Subtotal - totalCost) : invoice.Subtotal - totalCost) : 0;

        // القيد المحاسبي عبر المحرك المركزي
        var partyAccount = await PartyAccountAsync(invoice, isSales, ct);
        var description = $"{DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} - {invoice.PartyName}";
        PostingResult posted;
        if (isSales)
            posted = await _posting.PostSaleAsync(new SalePostingRequest
            {
                Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = invoice.Subtotal, VatAmount = invoice.VatTotal, CostAmount = totalCost, Payments = payments,
                RevenueAccountCode = invoice.RevenueAccountCode, InventoryAccountCode = invoice.InventoryAccountCode, CogsAccountCode = invoice.CogsAccountCode,
                CostCenterId = invoice.Items.Select(i => i.CostCenterId).FirstOrDefault(c => c.HasValue),
            }, ct);
        else
            posted = await _posting.PostPurchaseAsync(new PurchasePostingRequest
            {
                Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = invoice.Subtotal, VatAmount = invoice.VatTotal, Payments = payments,
                InventoryAccountCode = invoice.InventoryAccountCode,
                CostCenterId = invoice.Items.Select(i => i.CostCenterId).FirstOrDefault(c => c.HasValue),
            }, ct);
        invoice.JournalEntryId = posted.JournalEntryId;

        if (isSales)
            invoice.ZatcaQrCode = _zatca.GenerateTlvQr(tenant.NameAr, tenant.VatNumber,
                invoice.IssueDate.Date.Add(TimeSpan.TryParse(invoice.IssueTime, out var t) ? t : TimeSpan.Zero), invoice.GrandTotal, invoice.VatTotal);

        invoice.Status = "posted";
        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<PaymentPosting>> ResolvePaymentsAsync(Invoice invoice, CancellationToken ct)
    {
        var methods = await _db.Set<PaymentMethodItem>().AsNoTracking().ToListAsync(ct);
        if (invoice.PaymentSplits.Count > 0)
            return invoice.PaymentSplits.Where(s => s.Method != PaymentMethod.Credit)
                .Select(s => new PaymentPosting(TreasuryResolver.Resolve(s.Method, methods), s.Amount)).ToList();
        if (invoice.PaymentMethod == PaymentMethod.Credit || invoice.GrandTotal <= 0) return new();
        return new() { new PaymentPosting(TreasuryResolver.Resolve(invoice.PaymentMethod, methods), invoice.GrandTotal) };
    }

    private async Task<string?> PartyAccountAsync(Invoice invoice, bool isSales, CancellationToken ct)
    {
        if (!invoice.PartyId.HasValue) return null;
        return isSales
            ? await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == invoice.PartyId).Select(c => c.AccountCode).FirstOrDefaultAsync(ct)
            : await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == invoice.PartyId).Select(s => s.AccountCode).FirstOrDefaultAsync(ct);
    }

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
