
namespace ERP.Core.Models.Shared;

/// <summary>
/// دولة في قائمة المنشأة (تُعبّأ أول مرة بدول العالم وتُعدَّل). الرمز الثنائي ISO 3166-1 هو القيمة المحفوظة في
/// العملاء والموردين والبنوك والمنشأة والعنوان الوطني وفاتورة ZATCA، فلا يُغيَّر لدولة مستخدمة.
/// </summary>
public class Country : BaseEntity
{
    /// <summary>ISO 3166-1 alpha-2 (SA).</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-3 (SAU).</summary>
    public string? Code3 { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>اسم الجنسية (سعودي / Saudi) لملفات الموظفين.</summary>
    public string? NationalityAr { get; set; }
    public string? NationalityEn { get; set; }
    /// <summary>مفتاح الاتصال الدولي (+966).</summary>
    public string? DialCode { get; set; }
    /// <summary>رمز العملة ISO 4217 (SAR).</summary>
    public string? CurrencyCode { get; set; }
    /// <summary>الموقوفة لا تظهر في قوائم الاختيار وتبقى في السجلات القائمة.</summary>
    public bool IsActive { get; set; } = true;
}
