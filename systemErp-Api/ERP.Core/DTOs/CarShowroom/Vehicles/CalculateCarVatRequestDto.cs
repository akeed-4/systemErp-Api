namespace ERP.Core.DTOs.CarShowroom;

public class CalculateCarVatRequestDto
{
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public VatMode Mode { get; set; } = VatMode.Standard_15;
}
