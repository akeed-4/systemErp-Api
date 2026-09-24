using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarYearService : CrudService<CarYearModel, CarYearModelDto, CreateCarYearModelDto, UpdateCarYearModelDto>, ICarYearService
{
    public CarYearService(ErpDbContext db) : base(db) { }
    protected override string Label => "سنة الصنع";

    protected override IQueryable<CarYearModel> ApplyFilters(IQueryable<CarYearModel> q, PaginationParams p)
        => Guid.TryParse(p.Status, out var trimId) ? q.Where(y => y.TrimId == trimId) : q; // Status = معرّف الفئة

    protected override async Task ValidateAsync(CreateCarYearModelDto d, CarYearModel? existing, CancellationToken ct)
    {
        if (d.Year is < 1980 or > 2100) throw new ValidationFailedException("سنة الصنع غير صالحة.");
        if (!await Db.Set<CarTrim>().AnyAsync(t => t.Id == d.TrimId, ct)) throw new ValidationFailedException("الفئة غير موجودة.");
        if (await Db.Set<CarYearModel>().AnyAsync(y => y.TrimId == d.TrimId && y.Year == d.Year && (existing == null || y.Id != existing.Id), ct))
            throw new ConflictException("السنة مسجّلة مسبقاً لهذه الفئة.");
    }

    protected override async Task OnDeletingAsync(CarYearModel e, CancellationToken ct)
    {
        if (await Db.Set<Vehicle>().AnyAsync(v => v.YearId == e.Id, ct)) throw new ConflictException("لا يمكن حذف سنة عليها مركبات.");
    }
}
