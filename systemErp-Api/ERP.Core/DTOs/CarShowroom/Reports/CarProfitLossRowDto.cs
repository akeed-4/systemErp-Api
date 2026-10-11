namespace ERP.Core.DTOs.CarShowroom;

public class CarProfitLossRowDto
{
    public Guid Id { get; set; }
    public string? InvoiceNumber { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime SaleDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;
    public VehicleCondition Condition { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal ProfitAmount { get; set; }
    public bool IsProfit { get; set; }
    public decimal ProfitPercentage { get; set; }
    public decimal VatAmount { get; set; }
    public VatMode VatMode { get; set; }
    public string? SalesPerson { get; set; }
}
