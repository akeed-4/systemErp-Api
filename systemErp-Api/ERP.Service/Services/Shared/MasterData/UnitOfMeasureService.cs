using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// وحدات القياس بأنواعها (عدد، وزن، سائل، طول): لكل نوع وحدة أساس واحدة، وبقية وحدات النوع تُربط بها بمعامل تحويل.
/// أول وحدة في النوع تصبح أساسه. تغيير الأساس يكون بتفعيل «وحدة الأساس» على وحدة مربوطة بالأساس الحالي:
/// تُحوَّل بقية وحدات النوع (والأساس السابق) إليها بمعاملات محسوبة، فلا يتغير مقدار أي وحدة.
/// </summary>
public class UnitOfMeasureService : CrudService<UnitOfMeasure, UnitOfMeasureDto, CreateUnitOfMeasureDto, UpdateUnitOfMeasureDto>, IUnitOfMeasureService
{
    public UnitOfMeasureService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelUnit;

    /// <summary>تغيير أساس النوع المقرر في التحقق: الأساس السابق، ومعامل الوحدة الجديدة إليه (1 جديدة = Factor من السابقة).</summary>
    private (string OldBase, decimal Factor)? _rebase;

    protected override IQueryable<UnitOfMeasure> ApplySearch(IQueryable<UnitOfMeasure> q, string t)
        => q.Where(u => u.Code.Contains(t) || u.NameAr.Contains(t) || u.NameEn.Contains(t) || u.Symbol.Contains(t));

    protected override async Task ValidateAsync(CreateUnitOfMeasureDto d, UnitOfMeasure? existing, CancellationToken ct)
    {
        _rebase = null;
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Code)) errors.Add(Messages.CodeRequired);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ArabicNameRequired);
        if (d.ConversionFactor <= 0) errors.Add(Messages.ConversionFactorMustBePositive);
        d.UnitType = string.IsNullOrWhiteSpace(d.UnitType) ? UnitTypes.Count : d.UnitType.Trim().ToLowerInvariant();
        if (!UnitTypes.All.Contains(d.UnitType)) errors.Add(Messages.UnitTypeInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.Code && (existing == null || u.Id != existing.Id), ct))
            throw new ConflictException(Messages.UnitCodeInUse);

        // أساس النوع القائم (عدا هذه الوحدة)، والوحدات المربوطة بهذه الوحدة إن كانت أساساً
        var typeBase = await Db.Set<UnitOfMeasure>().AsNoTracking()
            .Where(u => u.IsBaseUnit && u.UnitType == d.UnitType && (existing == null || u.Id != existing.Id))
            .OrderBy(u => u.CreatedAt).Select(u => u.Code).FirstOrDefaultAsync(ct);
        var hasDependents = existing is { IsBaseUnit: true }
            && await Db.Set<UnitOfMeasure>().AnyAsync(u => u.BaseUnitCode == existing.Code && u.Id != existing.Id, ct);
        if (hasDependents && existing!.UnitType != d.UnitType) throw new ConflictException(Messages.UnitBaseTypeLocked);

        if (d.IsBaseUnit || typeBase == null)
        {
            if (!d.IsBaseUnit && existing is { IsBaseUnit: true }) throw new ValidationFailedException(Messages.UnitBaseCannotBeUnset);
            if (typeBase != null)
            {
                // ترقية وحدة مربوطة بأساس نوعها: يُعاد ربط النوع بها. وحدة جديدة أو غير مربوطة لا يُعرف معاملها إلى الأساس
                if (existing == null || existing.IsBaseUnit || existing.BaseUnitCode != typeBase || existing.UnitType != d.UnitType)
                    throw new ValidationFailedException(string.Format(Messages.UnitTypeAlreadyHasBase, typeBase));
                _rebase = (typeBase, existing.ConversionFactor);
            }
            d.IsBaseUnit = true;
            d.BaseUnitCode = null;
            d.ConversionFactor = 1;
            return;
        }

        // وحدة فرعية: الأساس من نوعها (الفارغ = أساس النوع)؛ إلغاء الأساس وحده لا يجوز (التغيير بترقية وحدة أخرى)
        if (existing is { IsBaseUnit: true }) throw new ValidationFailedException(Messages.UnitBaseCannotBeUnset);
        if (string.IsNullOrWhiteSpace(d.BaseUnitCode)) d.BaseUnitCode = typeBase;
        else if (d.BaseUnitCode != typeBase)
        {
            var exists = await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.BaseUnitCode, ct);
            throw new ValidationFailedException(exists ? string.Format(Messages.UnitBaseMustMatchType, d.BaseUnitCode) : Messages.BaseUnitNotFound);
        }
    }

    protected override async Task OnUpdatingAsync(UnitOfMeasure e, UpdateUnitOfMeasureDto d, CancellationToken ct)
    {
        if (_rebase is not { } plan) return;
        // 1 من الوحدة الجديدة = Factor من الأساس السابق: كل وحدة مربوطة بالسابق تُقسم معاملها عليه، والسابق يصبح 1 ÷ Factor
        foreach (var u in await Db.Set<UnitOfMeasure>().Where(u => u.Id != e.Id && (u.Code == plan.OldBase || u.BaseUnitCode == plan.OldBase)).ToListAsync(ct))
        {
            var factor = u.Code == plan.OldBase ? 1m : u.ConversionFactor;
            u.ConversionFactor = Math.Round(factor / plan.Factor, 8);
            u.IsBaseUnit = false;
            u.BaseUnitCode = e.Code;
        }
        _rebase = null;
    }

    protected override async Task OnDeletingAsync(UnitOfMeasure e, CancellationToken ct)
    {
        if (await Db.Set<Product>().AnyAsync(p => p.Unit == e.Code, ct))
            throw new ConflictException(Messages.UnitUsedByItems);
        if (await Db.Set<UnitOfMeasure>().AnyAsync(u => u.BaseUnitCode == e.Code, ct))
            throw new ConflictException(Messages.UnitIsBaseForOthers);
    }
}
