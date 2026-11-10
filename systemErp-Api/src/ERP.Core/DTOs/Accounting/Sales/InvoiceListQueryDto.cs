using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// مرشّحات قائمة الفواتير الخاصة بالمستند، تُضاف إلى <see cref="PaginationParams"/> (ترقيم/بحث/فرز/فترة/حالة).
/// كلها اختيارية؛ أسماء kind و hasVehicleLines كما كانت في عقد الـ API فلا يتأثر المستدعون الحاليون.
/// </summary>
public class InvoiceListQueryDto
{
    public InvoiceKind? Kind { get; set; }

    /// <summary>true = فواتير السيارات متعددة الأسطر فقط، false = بدونها، null = الكل.</summary>
    public bool? HasVehicleLines { get; set; }

    /// <summary>
    /// cash = كل طرق السداد الفوري (نقد/شبكة/تحويل)، credit = آجل. نفس تقسيم شاشات المبيعات والمشتريات.
    /// </summary>
    public string? PaymentType { get; set; }

    /// <summary>للمرتجعات: true = الفاتورة الأصلية فاتورة سيارات (لها أسطر مركبات)، false = بدونها.</summary>
    public bool? OriginalHasVehicleLines { get; set; }

    /// <summary>مطابقة تامة لرقم الفاتورة (للبحث السريع/الربط)، بخلاف SearchTerm الذي يطابق جزئيًا.</summary>
    public string? InvoiceNumber { get; set; }

    /// <summary>مطابقة تامة لنص رمز QR الخاص بالزكاة (قراءة الماسح عند ربط سند بفاتورة).</summary>
    public string? ZatcaQrCode { get; set; }
}
