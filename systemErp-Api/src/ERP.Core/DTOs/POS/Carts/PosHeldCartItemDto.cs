namespace ERP.Core.DTOs.POS;

public partial class PosHeldCartItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15;
    public decimal Discount { get; set; }
    public string? Note { get; set; }
}
