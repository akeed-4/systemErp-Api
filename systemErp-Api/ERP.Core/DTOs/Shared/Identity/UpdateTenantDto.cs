namespace ERP.Core.DTOs.Shared;

public partial class UpdateTenantDto : CreateTenantDto
{
    /// <summary>المنطقة الزمنية (IANA مثل Asia/Riyadh)؛ فارغ = تبقى الحالية.</summary>
    public string? TimeZoneId { get; set; }
    public bool? RequireDeliveryLocationReview { get; set; }

    // العنوان الوطني للمنشأة (يظهر في الفاتورة الإلكترونية ويشترطه الربط مع الهيئة). يُحرَّر من بيانات المنشأة ويُخزَّن في إعدادات
    // الهيئة حيث يقرؤه ملف الفاتورة. null = يبقى الحالي (الواجهات القديمة لا ترسله)، والنص الفارغ يمسحه.
    public string? Street { get; set; }
    /// <summary>رقم المبنى: 4 أرقام.</summary>
    public string? BuildingNumber { get; set; }
    public string? District { get; set; }
    /// <summary>الرمز البريدي: 5 أرقام.</summary>
    public string? PostalCode { get; set; }
}
