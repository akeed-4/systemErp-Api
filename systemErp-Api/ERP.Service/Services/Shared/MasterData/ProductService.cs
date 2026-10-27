using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ProductService : CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>, IProductService
{
    private readonly IAccountingPostingService _posting;
    private readonly IPermissionService _permissions;
    public ProductService(ErpDbContext db, IAccountingPostingService posting, IPermissionService permissions) : base(db)
    {
        _posting = posting; _permissions = permissions;
    }
    protected override string Label => Messages.LabelProduct;
    protected override bool Transactional => true;

    protected override IQueryable<Product> ApplySearch(IQueryable<Product> q, string t)
        => q.Where(p => p.Sku.Contains(t) || p.NameAr.Contains(t) || p.NameEn.Contains(t) || (p.Barcode != null && p.Barcode.Contains(t)));

    protected override IQueryable<Product> ApplyFilters(IQueryable<Product> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Category == p.Status); // Status = رمز التصنيف في شاشة الأصناف

    protected override async Task ValidateAsync(CreateProductDto d, Product? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Sku)) errors.Add(Messages.SkuRequired);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ProductArabicNameRequired);
        if (d.SellingPrice < 0 || d.StandardCost < 0 || d.MinStockLevel < 0) errors.Add(Messages.PricesAndReorderCannotBeNegative);
        if (d.VatRate is < 0 or > 100) errors.Add(Messages.VatRateRange);
        if (d.ExciseTaxRate is < 0 or > 100 || d.ExciseRetailPrice < 0 || d.ExciseStandardPrice < 0)
            errors.Add("نسبة الضريبة الانتقائية بين 0 و100 والأسعار المرجعية لا تكون سالبة.");
        if (d.ExciseTaxRate > 0 && d.ExciseRetailPrice <= 0 && d.ExciseStandardPrice <= 0)
            errors.Add("الصنف الخاضع للضريبة الانتقائية يحتاج سعر تجزئة محدداً أو سعراً قياسياً معتمداً.");
        if (!Enum.IsDefined(d.VatCategory)) errors.Add("التصنيف الضريبي غير صالح.");
        else if (d.VatCategory == VatCategory.MarginScheme) errors.Add(Messages.ProductMarginSchemeNotAllowed); // من بطاقة السيارة وحدها
        else if (d.VatCategory == VatCategory.Standard) d.VatExemptionReasonCode = null;
        else
        {
            // صنف صفري أو معفى أو خارج النطاق: بلا ضريبة، وسببه برمز الهيئة يطابق تصنيفه (يحمله كل سطر يبيع به)
            var reason = ERP.Service.Services.Accounting.VatExemptionReasons.Find(d.VatExemptionReasonCode);
            if (reason == null) errors.Add("سبب الصفرية أو الإعفاء مطلوب للصنف غير الخاضع للضريبة.");
            else if (reason.Category != d.VatCategory) errors.Add("سبب الإعفاء لا يطابق التصنيف الضريبي للصنف.");
            else d.VatExemptionReasonCode = reason.Code;
            d.VatRate = 0;
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Product>().AnyAsync(p => p.Sku == d.Sku && (existing == null || p.Id != existing.Id), ct))
            throw new ConflictException(Messages.SkuInUse);
        if (!string.IsNullOrWhiteSpace(d.Barcode)
            && await Db.Set<Product>().AnyAsync(p => p.Barcode == d.Barcode && (existing == null || p.Id != existing.Id), ct))
            throw new ConflictException(Messages.BarcodeInUse);
        if (!string.IsNullOrWhiteSpace(d.Category) && !await Db.Set<ProductCategory>().AnyAsync(c => c.Code == d.Category, ct))
            throw new ValidationFailedException(Messages.ProductCategoryNotFound);
        if (!string.IsNullOrWhiteSpace(d.Unit) && !await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.Unit, ct))
            throw new ValidationFailedException(Messages.UnitOfMeasureNotFound);
    }

    protected override Task OnCreatingAsync(Product e, CreateProductDto d, CancellationToken ct)
    {
        // الرصيد والتكلفة تتغيّر بحركات المخزون فقط؛ الرصيد الافتتاحي يدخل عبر حركة تسوية.
        e.CurrentStock = 0; e.AverageCost = 0; e.LastPurchaseCost = 0;
        return Task.CompletedTask;
    }

    protected override async Task OnUpdatingAsync(Product e, UpdateProductDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.CurrentStock = o.GetValue<decimal>(nameof(Product.CurrentStock));
        e.AverageCost = o.GetValue<decimal>(nameof(Product.AverageCost));
        e.LastPurchaseCost = o.GetValue<decimal>(nameof(Product.LastPurchaseCost));

        // التكلفة المعيارية لصنف دخل حركة أو مستنداً لا يغيّرها إلا صاحب صلاحية إعادة التقييم
        if (e.StandardCost != o.GetValue<decimal?>(nameof(Product.StandardCost)) && await IsUsedAsync(e.Id, ct)
            && !await _permissions.HasPermissionAsync(PermissionService.ItemStandardCostScreenId, ScreenAction.Edit, ct))
            throw new ForbiddenException(string.Format(Messages.StandardCostLockedForUsedItem, e.NameAr));

        // بالتكلفة المعيارية: تغيير المعيارية يعيد تسعير الرصيد القائم بها فوراً، وفرق قيمته قيد إعادة تقييم
        if (e.StandardCost is not { } standard || standard == e.AverageCost) return;
        var method = await Db.Set<CostingPolicy>().AsNoTracking().Select(p => (CostingMethod?)p.Method).FirstOrDefaultAsync(ct);
        if (method != CostingMethod.Standard) return;
        var before = InventoryValuation.Value(e);
        e.AverageCost = standard;
        await InventoryValuation.PostRevaluationAsync(Db, _posting, new[] { (e.Id, InventoryValuation.Value(e) - before) },
            $"إعادة تقييم {e.NameAr} بالتكلفة المعيارية {standard:0.####}", e.Id, ct);
    }

    protected override async Task OnCreatedAsync(Product e, CancellationToken ct) => await RecountAsync(ct);

    protected override async Task OnDeletingAsync(Product e, CancellationToken ct)
    {
        if (await Db.Set<StockMovement>().AnyAsync(m => m.ItemId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteProductWithMovements);
        if (await Db.Set<InvoiceItemBase>().AnyAsync(i => i.ItemId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteProductUsedInInvoices);
    }

    /// <summary>الصنف «مستخدم» إن دخل حركة مخزنية أو فاتورة (أو مرتجعها) أو نقطة بيع أو سند تسليم أو جرداً.</summary>
    public async Task<bool> IsUsedAsync(Guid id, CancellationToken ct = default)
        => await Db.Set<StockMovement>().AnyAsync(m => m.ItemId == id, ct)
            || await Db.Set<InvoiceItemBase>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<PosTransactionItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<PosSalesReturnItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<DeliveryNoteItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<DeliveryReturnItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<InventoryCountLine>().AnyAsync(l => l.ItemId == id, ct);

    public override async Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto dto, CancellationToken ct = default)
    {
        var result = await base.UpdateAsync(id, dto, ct);
        await RecountAsync(ct);
        return result;
    }

    public override async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await base.DeleteAsync(id, ct);
        await RecountAsync(ct);
    }

    private async Task RecountAsync(CancellationToken ct)
    {
        var counts = await Db.Set<Product>().GroupBy(p => p.Category).Select(g => new { Code = g.Key, N = g.Count() }).ToListAsync(ct);
        foreach (var c in await Db.Set<ProductCategory>().ToListAsync(ct))
            c.ItemCount = counts.FirstOrDefault(x => x.Code == c.Code)?.N ?? 0;
        await Db.SaveChangesAsync(ct);
    }
}
