namespace ERP.Core.DTOs.Accounting;

/// <summary>سطر في سجل التكلفة المعيارية لساعة الموظف بغرضه.</summary>
public class EmployeeCostRateDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeNameEn { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public decimal CostPerHour { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>السطر الساري اليوم لهذا الموظف وغرضه.</summary>
    public bool IsCurrent { get; set; }
}

public class CreateEmployeeCostRateDto
{
    public Guid EmployeeId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public decimal CostPerHour { get; set; }
    /// <summary>فارغ = اليوم (بتوقيت السعودية). مسموح ماضٍ أو مستقبلي (سعر مجدول).</summary>
    public DateTime? EffectiveFrom { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// موظف نشط في قائمة فنيي الورشة أو مشغّلي التأجير: بياناته الأساسية فقط (بلا راتب) وتكلفة ساعته المعيارية السارية للغرض.
/// </summary>
public class AddOnStaffDto
{
    public Guid EmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Phone { get; set; }
    public string? JobTitle { get; set; }
    public decimal? CurrentCostPerHour { get; set; }
}

/// <summary>
/// تسجيل موظف أساسي من إعدادات الإضافة (لمن لا يملك شؤون الموظفين): يُنشأ ملف الموظف نفسه بقواعده (الرمز والاسم والراتب الأساسي
/// إلزامية)، وتُضاف تكلفة ساعته المعيارية للغرض إن حُدِّدت. الرمز الفارغ يُولَّد.
/// </summary>
public class RegisterAddOnStaffDto
{
    public string Purpose { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Phone { get; set; }
    public string? NationalId { get; set; }
    public string? IdType { get; set; }
    public string? Nationality { get; set; }
    public string? JobTitle { get; set; }
    public DateTime? HireDate { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal? CostPerHour { get; set; }
}
