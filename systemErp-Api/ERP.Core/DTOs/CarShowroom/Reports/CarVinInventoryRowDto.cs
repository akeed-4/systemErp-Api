namespace ERP.Core.DTOs.CarShowroom;

public class CarVinInventoryRowDto
{
    public Guid Id { get; set; }
    public string BrandNameAr { get; set; } = string.Empty;
    public string? AgentNameAr { get; set; }
    public string ModelNameAr { get; set; } = string.Empty;
    public string? TrimNameAr { get; set; }
    public int Year { get; set; }
    public string ChassisNumber { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public VehicleCondition Condition { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SellingPrice { get; set; }
    public VatMode VatMode { get; set; }
    public VehicleStatus Status { get; set; }
    public int DaysInStock { get; set; }
}
