namespace ERP.Core.DTOs.POS;

public class CheckoutLineDto
{
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>خصم يدوي على السطر بالريال (يُقبل من الأدوار الإدارية فقط). الخصومات الأخرى تُحسب في الخادم من العروض.</summary>
    public decimal ManualDiscount { get; set; }
    public string? Note { get; set; }
}
