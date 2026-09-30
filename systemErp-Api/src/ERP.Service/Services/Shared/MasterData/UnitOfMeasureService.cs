using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class UnitOfMeasureService : CrudService<UnitOfMeasure, UnitOfMeasureDto, CreateUnitOfMeasureDto, UpdateUnitOfMeasureDto>, IUnitOfMeasureService
{
    public UnitOfMeasureService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelUnit;

    protected override IQueryable<UnitOfMeasure> ApplySearch(IQueryable<UnitOfMeasure> q, string t)
        => q.Where(u => u.Code.Contains(t) || u.NameAr.Contains(t) || u.NameEn.Contains(t) || u.Symbol.Contains(t));

    protected override async Task ValidateAsync(CreateUnitOfMeasureDto d, UnitOfMeasure? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Code)) errors.Add(Messages.CodeRequired);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ArabicNameRequired);
        if (d.ConversionFactor <= 0) errors.Add(Messages.ConversionFactorMustBePositive);
        if (!d.IsBaseUnit && string.IsNullOrWhiteSpace(d.BaseUnitCode)) errors.Add(Messages.NonBaseUnitNeedsBaseUnit);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.Code && (existing == null || u.Id != existing.Id), ct))
            throw new ConflictException(Messages.UnitCodeInUse);
        if (!d.IsBaseUnit && !await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.BaseUnitCode && u.IsBaseUnit, ct))
            throw new ValidationFailedException(Messages.BaseUnitNotFound);
    }

    protected override async Task OnDeletingAsync(UnitOfMeasure e, CancellationToken ct)
    {
        if (await Db.Set<Product>().AnyAsync(p => p.Unit == e.Code, ct))
            throw new ConflictException(Messages.UnitUsedByItems);
        if (await Db.Set<UnitOfMeasure>().AnyAsync(u => u.BaseUnitCode == e.Code, ct))
            throw new ConflictException(Messages.UnitIsBaseForOthers);
    }
}
