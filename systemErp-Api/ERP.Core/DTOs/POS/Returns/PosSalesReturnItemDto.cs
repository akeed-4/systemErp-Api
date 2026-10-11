namespace ERP.Core.DTOs.POS;

public partial class PosSalesReturnItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal SoldQuantity { get; set; }
    public decimal PreviouslyReturnedQuantity { get; set; }
    public decimal ReturnQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15;
    public decimal VatAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TotalWithVat { get; set; }
}
