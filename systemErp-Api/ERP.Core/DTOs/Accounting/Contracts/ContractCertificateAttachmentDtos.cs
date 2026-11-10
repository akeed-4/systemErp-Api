namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>بيانات مستند مؤرشف على بند المستخلص (بلا المحتوى؛ يُقرأ عند الفتح أو التنزيل).</summary>
public class CertificateItemAttachmentDto
{
    public Guid Id { get; set; }
    public Guid CertificateId { get; set; }
    public Guid ContractItemId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? UploadedBy { get; set; }
    /// <summary>تاريخ الأرشفة.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>هل يملك المستخدم الحالي حذفه (حسب حالة المستخلص وصلاحياته).</summary>
    public bool CanDelete { get; set; }
}

/// <summary>مرفقات مستخلص لكل بند مع صلاحيات المستخدم عليها.</summary>
public class CertificateAttachmentSummaryDto
{
    /// <summary>عدد المستندات لكل بند (معرّف بند جدول الكميات ← العدد).</summary>
    public Dictionary<Guid, int> Counts { get; set; } = new();
    public bool CanAdd { get; set; }
    public bool CanDelete { get; set; }
    public int MaxPerItem { get; set; }
    public long MaxBytes { get; set; }
}
