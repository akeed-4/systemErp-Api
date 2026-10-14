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
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.Code && (existing == null || u.Id != existing.Id), ct))
            throw new ConflictException(Messages.UnitCodeInUse);

        // وحدة أساس واحدة: أول وحدة تُعرَّف تصبح الأساس تلقائياً، وبقية الوحدات تُربط بها مباشرة
        // (رمز أساس فارغ أو غير موجود يُستبدل بالأساس الوحيد القائم بدل رفض الحفظ).
        var baseCodes = await Db.Set<UnitOfMeasure>()
            .Where(u => u.IsBaseUnit && (existing == null || u.Id != existing.Id))
            .Select(u => u.Code).ToListAsync(ct);
        if (d.IsBaseUnit || baseCodes.Count == 0)
        {
            d.IsBaseUnit = true;
            d.BaseUnitCode = null;
            d.ConversionFactor = 1;
            return;
        }
        if (string.IsNullOrWhiteSpace(d.BaseUnitCode) || !baseCodes.Contains(d.BaseUnitCode))
        {
            if (baseCodes.Count == 1) d.BaseUnitCode = baseCodes[0];
            else if (string.IsNullOrWhiteSpace(d.BaseUnitCode)) throw new ValidationFailedException(Messages.NonBaseUnitNeedsBaseUnit);
            else throw new ValidationFailedException(Messages.BaseUnitNotFound);
        }
    }

    protected override async Task OnDeletingAsync(UnitOfMeasure e, CancellationToken ct)
    {
        if (await Db.Set<Product>().AnyAsync(p => p.Unit == e.Code, ct))
            throw new ConflictException(Messages.UnitUsedByItems);
        if (await Db.Set<UnitOfMeasure>().AnyAsync(u => u.BaseUnitCode == e.Code, ct))
            throw new ConflictException(Messages.UnitIsBaseForOthers);
    }
}
