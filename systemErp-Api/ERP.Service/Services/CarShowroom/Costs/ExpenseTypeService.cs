using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class ExpenseTypeService : CrudService<ExpenseType, ExpenseTypeDto, CreateExpenseTypeDto, UpdateExpenseTypeDto>, IExpenseTypeService
{
    public ExpenseTypeService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelExpenseType;

    public override async Task<PagedResult<ExpenseTypeDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        await ExpenseTypeSeed.EnsureAsync(Db, ct);
        return await base.ListAsync(p, ct);
    }

    public override async Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        await ExpenseTypeSeed.EnsureAsync(Db, ct);
        return await base.LoadAsync(options, ct);
    }

    protected override IQueryable<ExpenseType> ApplySearch(IQueryable<ExpenseType> q, string t)
        => q.Where(x => x.Code.Contains(t) || x.NameAr.Contains(t) || x.NameEn.Contains(t));

    protected override IQueryable<ExpenseType> ApplyFilters(IQueryable<ExpenseType> q, PaginationParams p)
        => p.Status switch { "active" => q.Where(x => x.IsActive), "inactive" => q.Where(x => !x.IsActive), _ => q };

    protected override async Task ValidateAsync(CreateExpenseTypeDto d, ExpenseType? existing, CancellationToken ct)
    {
        d.Code = (d.Code ?? string.Empty).Trim().ToUpperInvariant();
        d.NameAr = (d.NameAr ?? string.Empty).Trim();
        d.NameEn = string.IsNullOrWhiteSpace(d.NameEn) ? d.NameAr : d.NameEn.Trim();
        d.AccountCode = string.IsNullOrWhiteSpace(d.AccountCode) ? null : d.AccountCode.Trim();

        var errors = new List<string>();
        if (d.Code.Length == 0) errors.Add(Messages.ExpenseTypeCodeRequired);
        if (d.NameAr.Length == 0) errors.Add(Messages.ExpenseTypeNameRequired);
        if (!Enum.IsDefined(d.Stage) || !Enum.IsDefined(d.DefaultTreatment) || !Enum.IsDefined(d.DefaultAllocationMethod) || !Enum.IsDefined(d.VatCategory)
            || d.VatCategory == VatCategory.MarginScheme) // هامش الربح لسطر السيارة وحده
            errors.Add(Messages.ExpenseTypeValuesInvalid);
        if (d.DefaultAmount < 0) errors.Add(Messages.ChargeAmountMustBePositive);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (existing is { IsSystem: true } && existing.Code != d.Code) throw new ConflictException(Messages.SystemExpenseTypeCodeFixed);
        if (await Db.Set<ExpenseType>().AnyAsync(t => t.Code == d.Code && (existing == null || t.Id != existing.Id), ct))
            throw new ConflictException(Messages.ExpenseTypeCodeExists);
        if (d.AccountCode != null && !await Db.Set<Account>().AnyAsync(a => a.Code == d.AccountCode, ct))
            throw new ValidationFailedException(Messages.ExpenseTypeAccountNotFound);
    }

    protected override async Task OnDeletingAsync(ExpenseType e, CancellationToken ct)
    {
        if (e.IsSystem) throw new ConflictException(Messages.SystemExpenseTypeCannotBeDeleted);
        if (await Db.Set<AdditionalChargeLine>().AnyAsync(l => l.ExpenseTypeId == e.Id, ct)) throw new ConflictException(Messages.ExpenseTypeInUse);
    }
}
