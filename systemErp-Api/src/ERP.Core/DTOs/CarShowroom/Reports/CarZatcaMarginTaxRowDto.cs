namespace ERP.Core.DTOs.CarShowroom;

public class CarZatcaMarginTaxRowDto
{
    public string ContractNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;
    public decimal PurchaseCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal GrossProfitMargin { get; set; }
    public decimal VatAmount15Percent { get; set; }
    public decimal TotalAmountCollected { get; set; }
    public ZatcaSubmissionStatus ZatcaComplianceStatus { get; set; }
}
