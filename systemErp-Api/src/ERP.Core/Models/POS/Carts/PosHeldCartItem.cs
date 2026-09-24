namespace ERP.Core.Models.POS;

public class PosHeldCartItem : BaseEntity
{
    public Guid HeldCartId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15;
    public decimal Discount { get; set; }
    public string? Note { get; set; }

    public virtual PosHeldCart HeldCart { get; set; } = null!;
}
