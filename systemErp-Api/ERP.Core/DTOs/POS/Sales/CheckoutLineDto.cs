namespace ERP.Core.DTOs.POS;

public class CheckoutLineDto
{
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>خصم يدوي على السطر بالريال (يُقبل من الأدوار الإدارية فقط). الخصومات الأخرى تُحسب في السرفر من العروض.</summary>
    public decimal ManualDiscount { get; set; }
    public string? Note { get; set; }
    /// <summary>بيع أوف لاين فقط: سعر الوحدة كما بيع على الجهاز من الكتالوج المحفوظ (قد يختلف عن السعر الحالي).</summary>
    public decimal? OfflineUnitPrice { get; set; }
}
