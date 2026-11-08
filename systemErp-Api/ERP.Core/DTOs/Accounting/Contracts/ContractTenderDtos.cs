namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>صف في سجل المناقصات: البيانات والموعد والقيم (من الخادم).</summary>
public class TenderRowDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? TenderNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    /// <summary>مرحلة العقد (بعد الترسية يتقدم العقد في دورته).</summary>
    public string Stage { get; set; } = string.Empty;
    public DateTime? IssueDate { get; set; }
    public DateTime? Deadline { get; set; }
    /// <summary>أيام حتى آخر موعد (سالب = فات) للمناقصة المفتوحة قبل التقديم؛ فارغ لغيرها.</summary>
    public int? DaysToDeadline { get; set; }
    public int? DurationMonths { get; set; }
    public decimal TenderValue { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal ContractValue { get; set; }
    public decimal EstimatedCost { get; set; }
    /// <summary>الهامش من قيمة العقد (فارغ بلا تكلفة).</summary>
    public decimal? MarginPercent { get; set; }
    public int ItemCount { get; set; }
    public int RevisionCount { get; set; }
    public DateTime? LastSubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? ResultNote { get; set; }
    public string? Requirements { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TenderRevisionDto
{
    public Guid Id { get; set; }
    public int RevisionNo { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public decimal TenderValue { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal ContractValue { get; set; }
    public decimal VatAmount { get; set; }
    public decimal EstimatedCost { get; set; }
    public string? Note { get; set; }
    public List<TenderRevisionLineDto> Lines { get; set; } = new();
}

public class TenderRevisionLineDto
{
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal TenderUnitPrice { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public bool IsSubItem { get; set; }
    public bool IsGroup { get; set; }
}

/// <summary>نقل المناقصة لحالة: الملاحظة إلزامية للخسارة، واختيارية للتقديم (وصف النسخة) والترسية.</summary>
public class MoveTenderDto
{
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
}
