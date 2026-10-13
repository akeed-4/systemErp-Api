using ERP.Core.Models.CarShowroom;

namespace ERP.Core.Models.Accounting;

/// <summary>
/// دفعة سيارات في فاتورة شراء: سطر واحد بالمواصفات المشتركة وسعر الوحدة، وشواصيه صفوف InvoiceVehicleLine (BatchId).
/// الكمية والإجماليات محسوبة من الشواصي في السرفر: الكمية = عددها، والإجمالي = مجموع أسعارها (سعر الدفعة أو السعر الخاص للشاسيه).
/// </summary>
public class InvoiceVehicleBatch : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }
    public string BrandNameAr { get; set; } = string.Empty;
    public string ModelNameAr { get; set; } = string.Empty;
    public string? TrimNameAr { get; set; }
    public int Year { get; set; }
    /// <summary>اللونان الافتراضيان لشواصي الدفعة؛ يُغيَّران لكل شاسيه.</summary>
    public string ColorExterior { get; set; } = string.Empty;
    public string ColorInterior { get; set; } = string.Empty;
    /// <summary>الفئة الضريبية للسطر (خاضع/معفى)؛ النسبة من إعدادات المعرض.</summary>
    public VatMode VatMode { get; set; } = VatMode.Standard_15;
    public decimal UnitPrice { get; set; }
    /// <summary>خصم الوحدة.</summary>
    public decimal Discount { get; set; }
    // تُحسب في السرفر دائمًا
    public int Quantity { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAfterVat { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
