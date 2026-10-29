namespace ERP.Core.DTOs.POS;

public class PosTerminalDto
{
    public Guid Id { get; set; }
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
    public bool AllowDiscount { get; set; }
    public decimal? MaxDiscountPercent { get; set; }
    public List<Guid> AllowedCategoryIds { get; set; } = new();
    public List<Guid> AllowedUserIds { get; set; } = new();
    public bool IsActive { get; set; }
}

public class PosTerminalUserDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class SavePosTerminalDto
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
    public List<Guid> AllowedCategoryIds { get; set; } = new();
    public List<Guid> AllowedUserIds { get; set; } = new();
    public bool IsActive { get; set; } = true;
}
