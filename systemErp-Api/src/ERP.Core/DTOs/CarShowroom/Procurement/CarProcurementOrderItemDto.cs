namespace ERP.Core.DTOs.CarShowroom;

public partial class CarProcurementOrderItemDto
{
    public Guid Id { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? TrimName { get; set; }
    public int Year { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal Total { get; set; }
    public string? Color { get; set; }
    public string? Transmission { get; set; }
    public string? FuelType { get; set; }
    public string? Notes { get; set; }
}
