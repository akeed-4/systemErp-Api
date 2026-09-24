using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarBrandService : CrudService<CarBrand, CarBrandDto, CreateCarBrandDto, UpdateCarBrandDto>, ICarBrandService
{
    public CarBrandService(ErpDbContext db) : base(db) { }
    protected override string Label => "الماركة";

    protected override IQueryable<CarBrand> ApplySearch(IQueryable<CarBrand> q, string t)
        => q.Where(b => b.NameAr.Contains(t) || b.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateCarBrandDto d, CarBrand? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.NameAr)) throw new ValidationFailedException("اسم الماركة بالعربية مطلوب.");
        if (await Db.Set<CarBrand>().AnyAsync(b => b.NameAr == d.NameAr && (existing == null || b.Id != existing.Id), ct))
            throw new ConflictException("الماركة مسجّلة مسبقاً.");
    }

    protected override async Task OnDeletingAsync(CarBrand e, CancellationToken ct)
    {
        if (await Db.Set<CarModel>().AnyAsync(m => m.BrandId == e.Id, ct)) throw new ConflictException("لا يمكن حذف ماركة لها موديلات.");
        if (await Db.Set<Vehicle>().AnyAsync(v => v.BrandId == e.Id, ct)) throw new ConflictException("لا يمكن حذف ماركة عليها مركبات.");
    }
}
