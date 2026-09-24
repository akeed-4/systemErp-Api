using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ProductService : CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>, IProductService
{
    public ProductService(ErpDbContext db) : base(db) { }
    protected override string Label => "الصنف";
    protected override bool Transactional => true;

    protected override IQueryable<Product> ApplySearch(IQueryable<Product> q, string t)
        => q.Where(p => p.Sku.Contains(t) || p.NameAr.Contains(t) || p.NameEn.Contains(t) || (p.Barcode != null && p.Barcode.Contains(t)));

    protected override IQueryable<Product> ApplyFilters(IQueryable<Product> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Category == p.Status); // Status = رمز التصنيف في شاشة الأصناف

    protected override async Task ValidateAsync(CreateProductDto d, Product? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Sku)) errors.Add("رمز الصنف (SKU) مطلوب.");
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add("اسم الصنف بالعربية مطلوب.");
        if (d.SellingPrice < 0 || d.StandardCost < 0 || d.MinStockLevel < 0) errors.Add("الأسعار وحد الطلب لا تكون سالبة.");
        if (d.VatRate is < 0 or > 100) errors.Add("نسبة الضريبة بين 0 و100.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Product>().AnyAsync(p => p.Sku == d.Sku && (existing == null || p.Id != existing.Id), ct))
            throw new ConflictException("رمز الصنف (SKU) مستخدم مسبقاً.");
        if (!string.IsNullOrWhiteSpace(d.Barcode)
            && await Db.Set<Product>().AnyAsync(p => p.Barcode == d.Barcode && (existing == null || p.Id != existing.Id), ct))
            throw new ConflictException("الباركود مستخدم مسبقاً.");
        if (!string.IsNullOrWhiteSpace(d.Category) && !await Db.Set<ProductCategory>().AnyAsync(c => c.Code == d.Category, ct))
            throw new ValidationFailedException("تصنيف الصنف غير موجود.");
        if (!string.IsNullOrWhiteSpace(d.Unit) && !await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.Unit, ct))
            throw new ValidationFailedException("وحدة القياس غير موجودة.");
    }

    protected override Task OnCreatingAsync(Product e, CreateProductDto d, CancellationToken ct)
    {
        // الرصيد والتكلفة تتغيّر بحركات المخزون فقط؛ الرصيد الافتتاحي يدخل عبر حركة تسوية.
        e.CurrentStock = 0; e.AverageCost = 0; e.LastPurchaseCost = 0;
        return Task.CompletedTask;
    }

    protected override Task OnUpdatingAsync(Product e, UpdateProductDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.CurrentStock = o.GetValue<decimal>(nameof(Product.CurrentStock));
        e.AverageCost = o.GetValue<decimal>(nameof(Product.AverageCost));
        e.LastPurchaseCost = o.GetValue<decimal>(nameof(Product.LastPurchaseCost));
        return Task.CompletedTask;
    }

    protected override async Task OnCreatedAsync(Product e, CancellationToken ct) => await RecountAsync(ct);

    protected override async Task OnDeletingAsync(Product e, CancellationToken ct)
    {
        if (await Db.Set<StockMovement>().AnyAsync(m => m.ItemId == e.Id, ct))
            throw new ConflictException("لا يمكن حذف صنف له حركات مخزون.");
        if (await Db.Set<InvoiceItem>().AnyAsync(i => i.ItemId == e.Id, ct))
            throw new ConflictException("لا يمكن حذف صنف مستخدم في فواتير.");
    }

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
