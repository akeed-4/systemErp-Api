namespace ERP.Core.DTOs.CarShowroom;

public class CarProcurementTrackingRowDto
{
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string BrandAndModel { get; set; } = string.Empty;
    public int RequestedQty { get; set; }
    public int ReceivedVinQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal GrandTotal { get; set; }
    public ProcurementStage CurrentStage { get; set; }
    public string PaymentType { get; set; } = string.Empty;
}
