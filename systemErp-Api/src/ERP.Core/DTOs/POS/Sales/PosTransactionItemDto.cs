namespace ERP.Core.DTOs.POS;

public partial class PosTransactionItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? UnitAr { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15;
    public decimal VatAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalWithVat { get; set; }
}
