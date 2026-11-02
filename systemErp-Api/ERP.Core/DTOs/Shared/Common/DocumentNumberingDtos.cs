namespace ERP.Core.DTOs.Shared;

/// <summary>إعداد ترقيم نوع مستند كما يُعرض في الشاشة، مع العدّاد الفعلي والرقم التالي المتوقع.</summary>
public class DocumentNumberingSettingDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string DefaultPrefix { get; set; } = string.Empty;
    /// <summary>النوع يدعم الترقيم اليدوي.</summary>
    public bool ManualAllowed { get; set; }
    /// <summary>auto | manual</summary>
    public string Mode { get; set; } = "auto";
    /// <summary>فارغ = إعداد المنشأة العام | tenant | warehouse</summary>
    public string? Scope { get; set; }
    /// <summary>النطاق المطبَّق فعلاً (بعد الرجوع لإعداد المنشأة): tenant | warehouse | user.</summary>
    public string EffectiveScope { get; set; } = "tenant";
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; } = 6;
    public bool ResetYearly { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>الرقم التالي في العدّاد المعروض (للمستودع والسنة المالية الحالية).</summary>
    public long NextNumber { get; set; }
    /// <summary>معاينة الرقم التالي كاملاً.</summary>
    public string NextFormatted { get; set; } = string.Empty;
    public int? FiscalYear { get; set; }
}

/// <summary>تعديل إعداد ترقيم نوع مستند. NextNumber اختياري ويُطبَّق على عدّاد المستودع المحدد (أو المنشأة) للسنة الحالية.</summary>
public class UpdateDocumentNumberingDto
{
    public string Mode { get; set; } = "auto";
    public string? Scope { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int Padding { get; set; } = 6;
    public bool ResetYearly { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>زيادة الرقم التالي فقط (لا يُسمح بإنقاصه).</summary>
    public long? NextNumber { get; set; }
    /// <summary>المستودع الذي يُعرض/يُعدَّل عدّاده عند الترقيم لكل مستودع.</summary>
    public Guid? WarehouseId { get; set; }
}
