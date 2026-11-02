using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// معرض/مستودع السيارة: تدخل معرض فاتورة شرائها أو استلامها، وتُباع من معرضها، وتعود لمعرض مرتجع بيعها،
/// وتنتقل بالتحويل بين المعارض أو باعتماد جرد وُجدت فيه بمعرض آخر.
/// </summary>
internal static class VehicleSites
{
    /// <summary>يضع السيارات في المعرض المحدد، أو معرض الجلسة/الافتراضي عند عدم التحديد.</summary>
    public static async Task<Warehouse> PlaceAsync(ErpDbContext db, IEnumerable<Vehicle> vehicles, Guid? warehouseId, CancellationToken ct)
    {
        var warehouse = await WarehouseStocks.ResolveAsync(db, warehouseId, ct);
        foreach (var v in vehicles) Place(v, warehouse.Id, warehouse.NameAr);
        return warehouse;
    }

    public static void Place(Vehicle v, Guid warehouseId, string? warehouseName)
    {
        v.WarehouseId = warehouseId;
        v.WarehouseName = warehouseName;
    }

    /// <summary>
    /// البيع من معرض السيارة: سيارة في معرض غير معرض الفاتورة تُرفض (تُحوَّل أولاً أو يُختار معرضها).
    /// سيارة قديمة بلا معرض تُقبل وتُنسب لمعرض الفاتورة.
    /// </summary>
    public static void EnsureSoldFrom(Vehicle v, Guid? invoiceWarehouseId, string? invoiceWarehouseName)
    {
        if (!invoiceWarehouseId.HasValue) return;
        if (v.WarehouseId.HasValue && v.WarehouseId != invoiceWarehouseId)
            throw new ConflictException(string.Format(Messages.VehicleInOtherWarehouse, v.ChassisNumber, v.WarehouseName, invoiceWarehouseName));
        Place(v, invoiceWarehouseId.Value, invoiceWarehouseName);
    }
}
