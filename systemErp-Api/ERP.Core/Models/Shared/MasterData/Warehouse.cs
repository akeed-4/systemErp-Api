
namespace ERP.Core.Models.Shared;

public class Warehouse : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
    public string Status { get; set; } = "active"; // active | inactive
    /// <summary>حساب مخزون المستودع؛ فارغ = حساب المخزون في إعدادات الحسابات. الحساب اليدوي في المستند يتقدم عليه.</summary>
    public string? InventoryAccountCode { get; set; }
}
