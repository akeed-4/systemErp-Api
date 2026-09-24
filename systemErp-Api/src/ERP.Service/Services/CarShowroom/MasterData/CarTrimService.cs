using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarTrimService : CrudService<CarTrim, CarTrimDto, CreateCarTrimDto, UpdateCarTrimDto>, ICarTrimService
{
    public CarTrimService(ErpDbContext db) : base(db) { }
    protected override string Label => "الفئة";

    protected override IQueryable<CarTrim> ApplySearch(IQueryable<CarTrim> q, string t)
        => q.Where(x => x.NameAr.Contains(t) || (x.NameEn != null && x.NameEn.Contains(t)));

    protected override IQueryable<CarTrim> ApplyFilters(IQueryable<CarTrim> q, PaginationParams p)
        => Guid.TryParse(p.Status, out var modelId) ? q.Where(t => t.ModelId == modelId) : q; // Status = معرّف الموديل

    protected override async Task ValidateAsync(CreateCarTrimDto d, CarTrim? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.NameAr)) throw new ValidationFailedException("اسم الفئة بالعربية مطلوب.");
        if (!await Db.Set<CarModel>().AnyAsync(m => m.Id == d.ModelId, ct)) throw new ValidationFailedException("الموديل غير موجود.");
        if (await Db.Set<CarTrim>().AnyAsync(t => t.ModelId == d.ModelId && t.NameAr == d.NameAr && (existing == null || t.Id != existing.Id), ct))
            throw new ConflictException("الفئة مسجّلة مسبقاً لهذا الموديل.");
    }

    protected override async Task OnDeletingAsync(CarTrim e, CancellationToken ct)
    {
        if (await Db.Set<CarYearModel>().AnyAsync(y => y.TrimId == e.Id, ct)) throw new ConflictException("لا يمكن حذف فئة لها سنوات صنع.");
        if (await Db.Set<Vehicle>().AnyAsync(v => v.TrimId == e.Id, ct)) throw new ConflictException("لا يمكن حذف فئة عليها مركبات.");
    }
}
