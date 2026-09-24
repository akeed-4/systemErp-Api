using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ProductCategoryService : CrudService<ProductCategory, ProductCategoryDto, CreateProductCategoryDto, UpdateProductCategoryDto>, IProductCategoryService
{
    public ProductCategoryService(ErpDbContext db) : base(db) { }
    protected override string Label => "التصنيف";

    protected override IQueryable<ProductCategory> ApplySearch(IQueryable<ProductCategory> q, string t)
        => q.Where(c => c.Code.Contains(t) || c.NameAr.Contains(t) || c.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateProductCategoryDto d, ProductCategory? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException("الكود والاسم بالعربية مطلوبان.");
        if (await Db.Set<ProductCategory>().AnyAsync(c => c.Code == d.Code && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException("كود التصنيف مستخدم مسبقاً.");
    }

    protected override async Task OnUpdatingAsync(ProductCategory e, UpdateProductCategoryDto d, CancellationToken ct)
        => e.ItemCount = await Db.Set<Product>().CountAsync(p => p.Category == e.Code, ct); // العدّاد محسوب لا مُدخَل

    protected override async Task OnDeletingAsync(ProductCategory e, CancellationToken ct)
    {
        if (await Db.Set<Product>().AnyAsync(p => p.Category == e.Code, ct))
            throw new ConflictException("التصنيف مستخدم في أصناف.");
    }
}
