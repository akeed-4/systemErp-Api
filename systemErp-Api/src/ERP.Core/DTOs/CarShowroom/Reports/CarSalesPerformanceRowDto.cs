namespace ERP.Core.DTOs.CarShowroom;

public class CarSalesPerformanceRowDto
{
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public CarSalesCycleType CycleType { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerNationalIdOrCr { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public VehicleCondition Condition { get; set; }
    public string? FinancingBankName { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal ProfitMargin { get; set; }
    public VatMode VatMode { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }
}
