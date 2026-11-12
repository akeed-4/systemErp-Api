namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>«إنشاء العقد» من الدراسة والخطة المعتمدتين: رقم العقد تلقائي، أو يدوي إن سمح الترقيم.</summary>
public class CreateContractFromStudyDto
{
    public string? ContractNumber { get; set; }
}

/// <summary>صف في سجل دراسات الجدوى: الرقم والمشروع والعميل والقيم والهامش والتسعير وحالة الخطة والعقد الناتج.</summary>
public class FeasibilityStudyRowDto
{
    public Guid Id { get; set; }
    public string FeasibilityNumber { get; set; } = string.Empty;
    /// <summary>study | plan_approved | contracted.</summary>
    public string FeasibilityStatus { get; set; } = string.Empty;
    /// <summary>رقم العقد بعد «إنشاء العقد» (فارغ قبله).</summary>
    public string? ContractNumber { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal ContractValue { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal Margin { get; set; }
    public decimal MarginPercent { get; set; }
    public int LineCount { get; set; }
    /// <summary>أسطر مسعّرة من مكتبة التكاليف (تحليل مجمّد).</summary>
    public int PricedLineCount { get; set; }
    /// <summary>حالة خطة التنفيذ (فارغة = لم تُجهَّز).</summary>
    public string? PlanStatus { get; set; }
    public DateTime CreatedAt { get; set; }
}
