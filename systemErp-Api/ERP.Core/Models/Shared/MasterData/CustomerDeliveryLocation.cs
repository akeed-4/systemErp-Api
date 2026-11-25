
namespace ERP.Core.Models.Shared;

/// <summary>
/// موقع توصيل للعميل (مواقع متعددة، وموقع افتراضي فعال واحد). الموقع المستخدم في بيان تسليم يُعطَّل ولا يُحذف،
/// والبيان يحفظ لقطة مستقلة منه (العنوان والإحداثيات) لا تتغير بتعديله لاحقاً.
/// </summary>
public class CustomerDeliveryLocation : BaseEntity
{
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? District { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}
