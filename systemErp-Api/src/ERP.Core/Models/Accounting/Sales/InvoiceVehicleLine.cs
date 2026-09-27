using ERP.Core.Models.CarShowroom;

namespace ERP.Core.Models.Accounting;

/// <summary>
/// سطر سيارة داخل فاتورة شراء/بيع (سطر لكل سيارة). مصدر الحقيقة لتفاصيل السيارة داخل المستند؛
/// أما الأثر المحاسبي فيمرّ عبر InvoiceItem المشتق منه (ItemId = Empty) فتعمل الإجماليات والقيود بنفس محرك الفوترة.
/// شراء: تُنشأ المركبة (VehicleId) عند الترحيل. بيع: VehicleId مركبة موجودة تصير Sold عند الترحيل.
/// </summary>
public class InvoiceVehicleLine : BaseEntity
{
    public Guid InvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid? VehicleId { get; set; }
    /// <summary>رقم الشاسيه؛ قد يكون فارغًا في مسودة الشراء فقط.</summary>
    public string? Vin { get; set; }
    /// <summary>مرجع مؤقت (TEMP-0001) عند غياب الـVIN — ليس رقم شاسيه حقيقيًا ولا يمنع منع الترحيل.</summary>
    public string? TempRef { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }
    public string BrandNameAr { get; set; } = string.Empty;
    public string ModelNameAr { get; set; } = string.Empty;
    public string? TrimNameAr { get; set; }
    public int Year { get; set; }
    public string ColorExterior { get; set; } = string.Empty;
    public string ColorInterior { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }
    public VatMode VatMode { get; set; } = VatMode.Standard_15;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    // تُحسب في الخادم دائمًا
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    /// <summary>بيع فقط: تكلفة المركبة وقت الحفظ.</summary>
    public decimal UnitCost { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}
