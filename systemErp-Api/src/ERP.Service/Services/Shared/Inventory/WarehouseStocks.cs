using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// أرصدة الأصناف في المستودعات: تحديد المستودع الفعلي لمستند (الافتراضي عند عدم التحديد) وتعديل رصيد الصنف فيه.
/// كل تغيير في رصيد مستودع يمر من هنا ليبقى مجموع المستودعات مساوياً لرصيد الصنف.
/// </summary>
public static class WarehouseStocks
{
    /// <summary>المستودع المحدَّد، أو الافتراضي للمنشأة عند عدم التحديد (يُنشأ المستودع الرئيسي إن لم يوجد أي مستودع).</summary>
    public static async Task<Warehouse> ResolveAsync(ErpDbContext db, Guid? warehouseId, CancellationToken ct)
    {
        if (warehouseId.HasValue)
            return await db.Set<Warehouse>().FirstOrDefaultAsync(w => w.Id == warehouseId, ct)
                ?? throw new ValidationFailedException(Messages.WarehouseNotFound);

        var warehouse = await db.Set<Warehouse>().OrderByDescending(w => w.IsDefault).ThenBy(w => w.CreatedAt).FirstOrDefaultAsync(ct);
        if (warehouse != null) return warehouse;

        warehouse = new Warehouse { Code = "WH-001", NameAr = "المستودع الرئيسي", NameEn = "Main Warehouse", IsDefault = true };
        db.Add(warehouse);
        await db.SaveChangesAsync(ct);
        return warehouse;
    }

    public static async Task<decimal> QuantityAsync(ErpDbContext db, Guid itemId, Guid warehouseId, CancellationToken ct)
        => (await FindAsync(db, itemId, warehouseId, ct))?.Quantity ?? 0;

    /// <summary>يزيد/ينقص رصيد الصنف في المستودع ويُرجع الرصيد بعد التغيير (يُحفظ مع حفظ المستدعي).</summary>
    public static async Task<decimal> ApplyAsync(ErpDbContext db, Guid itemId, Guid warehouseId, decimal delta, CancellationToken ct)
    {
        var row = await FindAsync(db, itemId, warehouseId, ct);
        if (row == null)
        {
            row = new WarehouseStock { ItemId = itemId, WarehouseId = warehouseId };
            db.Add(row);
        }
        row.Quantity += delta;
        return row.Quantity;
    }

    private static async Task<WarehouseStock?> FindAsync(ErpDbContext db, Guid itemId, Guid warehouseId, CancellationToken ct)
        => db.Set<WarehouseStock>().Local.FirstOrDefault(s => s.ItemId == itemId && s.WarehouseId == warehouseId)
            ?? await db.Set<WarehouseStock>().FirstOrDefaultAsync(s => s.ItemId == itemId && s.WarehouseId == warehouseId, ct);
}
