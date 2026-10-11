using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class WarehouseService : CrudService<Warehouse, WarehouseDto, CreateWarehouseDto, UpdateWarehouseDto>, IWarehouseService
{
    public WarehouseService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelWarehouse;

    protected override IQueryable<Warehouse> ApplySearch(IQueryable<Warehouse> q, string t)
        => q.Where(w => w.Code.Contains(t) || w.NameAr.Contains(t) || w.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateWarehouseDto d, Warehouse? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (await Db.Set<Warehouse>().AnyAsync(w => w.Code == d.Code && (existing == null || w.Id != existing.Id), ct))
            throw new ConflictException(Messages.WarehouseCodeInUse);
        if (existing is { IsDefault: true } && !d.IsDefault)
            throw new ConflictException(Messages.SetAnotherDefaultWarehouse);
    }

    protected override async Task OnCreatingAsync(Warehouse e, CreateWarehouseDto d, CancellationToken ct)
    {
        var any = await Db.Set<Warehouse>().AnyAsync(ct);
        if (!any) e.IsDefault = true; // أول مستودع هو الافتراضي
        if (e.IsDefault) await ClearOtherDefaultsAsync(e.Id, ct);
    }

    protected override async Task OnUpdatingAsync(Warehouse e, UpdateWarehouseDto d, CancellationToken ct)
    {
        if (e.IsDefault) await ClearOtherDefaultsAsync(e.Id, ct);
    }

    private async Task ClearOtherDefaultsAsync(Guid keep, CancellationToken ct)
    {
        foreach (var w in await Db.Set<Warehouse>().Where(w => w.IsDefault && w.Id != keep).ToListAsync(ct)) w.IsDefault = false;
    }

    protected override async Task OnDeletingAsync(Warehouse e, CancellationToken ct)
    {
        if (e.IsDefault) throw new ConflictException(Messages.CannotDeleteDefaultWarehouse);
        if (await Db.Set<StockMovement>().AnyAsync(m => m.WarehouseId == e.Id, ct))
            throw new ConflictException(Messages.WarehouseHasMovements);
    }
}
