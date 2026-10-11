using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarModelService : CrudService<CarModel, CarModelDto, CreateCarModelDto, UpdateCarModelDto>, ICarModelService
{
    public CarModelService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelModel;

    protected override IQueryable<CarModel> ApplySearch(IQueryable<CarModel> q, string t)
        => q.Where(m => m.NameAr.Contains(t) || (m.NameEn != null && m.NameEn.Contains(t)));

    protected override IQueryable<CarModel> ApplyFilters(IQueryable<CarModel> q, PaginationParams p)
        => Guid.TryParse(p.Status, out var brandId) ? q.Where(m => m.BrandId == brandId) : q; // Status = معرّف الماركة

    protected override async Task ValidateAsync(CreateCarModelDto d, CarModel? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.NameAr)) throw new ValidationFailedException(Messages.ModelArabicNameRequired);
        if (!await Db.Set<CarBrand>().AnyAsync(b => b.Id == d.BrandId, ct)) throw new ValidationFailedException(Messages.BrandNotFound);
        if (d.AgentId.HasValue && !await Db.Set<CarAgent>().AnyAsync(a => a.Id == d.AgentId, ct)) throw new ValidationFailedException(Messages.AgentNotFound);
        if (await Db.Set<CarModel>().AnyAsync(m => m.BrandId == d.BrandId && m.NameAr == d.NameAr && (existing == null || m.Id != existing.Id), ct))
            throw new ConflictException(Messages.ModelAlreadyExistsForBrand);
    }

    protected override async Task OnDeletingAsync(CarModel e, CancellationToken ct)
    {
        if (await Db.Set<CarTrim>().AnyAsync(t => t.ModelId == e.Id, ct)) throw new ConflictException(Messages.CannotDeleteModelWithTrims);
        if (await Db.Set<Vehicle>().AnyAsync(v => v.ModelId == e.Id, ct)) throw new ConflictException(Messages.CannotDeleteModelWithVehicles);
    }
}
