namespace ERP.Core.Models.Shared;

/// <summary>نطاق الجرد: أصناف المخزون العام (كميات) أو مركبات المعرض (هوية VIN).</summary>
public enum InventoryCountScope
{
    Items = 1,
    Vehicles = 2
}

/// <summary>نوع الجرد: شامل، دوري لجزء من الأصناف (Cycle)، أو جرد نهاية فترة. المنطق واحد والنوع للتصنيف والتقارير.</summary>
public enum InventoryCountType
{
    Full = 1,
    Cycle = 2,
    Periodic = 3
}

public enum InventoryCountStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5
}

public enum InventoryCountApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4
}

/// <summary>أثر اعتماد سطر الجرد: تسوية كمية، شطب/استعادة مركبة، نقل موقعها، أو فرق مُثبت دون تسوية آلية.</summary>
public enum InventoryCountLineResolution
{
    None = 0,
    StockIn = 1,
    StockOut = 2,
    VehicleWrittenOff = 3,
    VehicleReinstated = 4,
    VehicleRelocated = 5,
    RequiresRegistration = 6,
    NotAdjusted = 7
}
