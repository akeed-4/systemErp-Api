namespace ERP.Core.DTOs.Shared;

/// <summary>صف من ملف استيراد العملاء/الموردين: رقمه في Excel (لرسائل الأخطاء) وبياناته بعد ربط الأعمدة.</summary>
public class PartyImportRowInput<T>
{
    public int RowNumber { get; set; }
    public T Data { get; set; } = default!;
}

/// <summary>نتيجة فحص صف قبل الحفظ: valid يُستورد، warning يُستورد مع تنبيه (تكرار محتمل)، error لا يُستورد.</summary>
public class PartyImportRowDto
{
    public int RowNumber { get; set; }
    /// <summary>valid | warning | error</summary>
    public string Status { get; set; } = "valid";
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    /// <summary>الهاتف بعد توحيد صيغته (كما سيُحفظ).</summary>
    public string? Phone { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>معاينة الاستيراد: فحص كل الصفوف في السرفر دون حفظ شيء.</summary>
public class PartyImportPreviewDto
{
    public int TotalRows { get; set; }
    public int ValidCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public List<PartyImportRowDto> Rows { get; set; } = new();
}
