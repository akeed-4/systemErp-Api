namespace ERP.Core.Models.POS;

/// <summary>تعريف محطة بيع فعلية وسياساتها. القيم تنسخ إلى الوردية عند فتحها حتى لا تتغير صلاحياتها أثناء العمل.</summary>
public class PosTerminal : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DeviceIdentifier { get; set; } = string.Empty;
    public string HardwareType { get; set; } = "computer";
    public string? HardwareModel { get; set; }
    public string? HardwareSerialNumber { get; set; }
    public string ConnectionType { get; set; } = "browser";
    public Guid? WarehouseId { get; set; }
    public string? PrinterModel { get; set; }
    public string PrinterConnectionType { get; set; } = "browser";
    public string? PrinterAddress { get; set; }
    public bool HasCashDrawer { get; set; }
    public bool AllowReturns { get; set; }
    public bool AllowDiscount { get; set; } = true;
    public decimal? MaxDiscountPercent { get; set; }
    public string AllowedCategoryIdsJson { get; set; } = "[]";
    public string AllowedUserIdsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
}
