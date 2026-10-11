namespace ERP.Core.DTOs.CarShowroom;

public class CarSupplierProcurementRowDto
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierTaxNumber { get; set; }
    public string? SupplierPhone { get; set; }
    public int TotalOrdersCount { get; set; }
    public int VehiclesCount { get; set; }
    public decimal TotalPurchaseAmount { get; set; }
    public int CompletedOrdersCount { get; set; }
    public int InProgressOrdersCount { get; set; }
    public decimal BalanceDue { get; set; }
    public DateTime? LastOrderDate { get; set; }
}
