namespace ERP.Core.DTOs.Shared;

public partial class WarehouseDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    /// <summary>الفرع الذي يتبعه الموقع.</summary>
    public Guid BranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active";
    public string? InventoryAccountCode { get; set; }
    /// <summary>حسابات الموقع الخاصة (للعرض؛ تُنشأ تلقائياً ولا تُرسَل في الحفظ).</summary>
    public string? SalesAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }
    public string? CashAccountCode { get; set; }
    public string? ShortageAccountCode { get; set; }
    public string? SurplusAccountCode { get; set; }
}
