namespace ERP.Core.DTOs.Shared;

public class InventoryCountDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public InventoryCountScope Scope { get; set; }
    public InventoryCountType CountType { get; set; }
    public InventoryCountStatus Status { get; set; }
    public DateTime CountDate { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? Location { get; set; }
    public string CountedBy { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? RejectionReason { get; set; }
    public int TotalLines { get; set; }
    public int CountedLines { get; set; }
    public int VarianceLines { get; set; }
    public decimal TotalSurplusValue { get; set; }
    public decimal TotalShortageValue { get; set; }
    public decimal NetVarianceValue { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? JournalEntryNumber { get; set; }
    public string? Notes { get; set; }
    public List<InventoryCountLineDto> Lines { get; set; } = new();
}

public class InventoryCountLineDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string? Sku { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public Guid? VehicleId { get; set; }
    public string? ChassisNumber { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public decimal VarianceQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal VarianceValue { get; set; }
    public InventoryCountLineResolution Resolution { get; set; }
    public string? Notes { get; set; }
}

/// <summary>ما يُدخله المستخدم؛ الرقم والحالة ورصيد النظام والتكلفة والفروق يحسبها الخادم.</summary>
public class CreateInventoryCountDto
{
    public InventoryCountScope Scope { get; set; } = InventoryCountScope.Items;
    public InventoryCountType CountType { get; set; } = InventoryCountType.Full;
    public DateTime CountDate { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Location { get; set; }
    public string CountedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<InventoryCountLineInputDto> Lines { get; set; } = new();
}

public class UpdateInventoryCountDto : CreateInventoryCountDto
{
}

/// <summary>سطر مُدخل: ItemId للأصناف أو ChassisNumber للمركبات، مع الكمية المعدودة (null = لم يُعدّ).</summary>
public class InventoryCountLineInputDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string? ChassisNumber { get; set; }
    public decimal? CountedQuantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>طلب تجهيز أسطر الجرد من رصيد النظام الحالي (قائمة المتوقَّع).</summary>
public class InventoryCountSnapshotRequestDto
{
    public InventoryCountScope Scope { get; set; } = InventoryCountScope.Items;
    /// <summary>للأصناف: كود التصنيف (جرد دوري لجزء من الأصناف).</summary>
    public string? Category { get; set; }
    /// <summary>للأصناف: المستودع المجرود (فارغ = الافتراضي).</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary>للأصناف: تضمين الأصناف ذات الرصيد الصفري.</summary>
    public bool IncludeZeroStock { get; set; }
    /// <summary>للمركبات: موقع المعرض.</summary>
    public string? Location { get; set; }
}
