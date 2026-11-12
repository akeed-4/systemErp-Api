namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>بيانات مستند مؤرشف على بند التقرير اليومي (بلا المحتوى؛ يُقرأ عند الفتح أو التنزيل).</summary>
public class SiteReportItemAttachmentDto
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid ContractItemId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? UploadedBy { get; set; }
    /// <summary>تاريخ الأرشفة.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>هل يملك المستخدم الحالي حذفه (حسب حالة التقرير وصلاحياته).</summary>
    public bool CanDelete { get; set; }
}

/// <summary>مرفقات التقرير اليومي لكل بند مع صلاحيات المستخدم عليها وحدود الرفع.</summary>
public class SiteReportAttachmentSummaryDto
{
    /// <summary>عدد المستندات لكل بند (معرّف بند جدول الكميات ← العدد).</summary>
    public Dictionary<Guid, int> Counts { get; set; } = new();
    public bool CanAdd { get; set; }
    public bool CanDelete { get; set; }
    public int MaxPerItem { get; set; }
    public long MaxBytes { get; set; }
}
