namespace ERP.Core.DTOs.Shared;

/// <summary>بيانات مرفق مشترك (بلا المحتوى؛ يُقرأ عند الفتح أو التنزيل).</summary>
public class DocumentAttachmentDto
{
    public Guid Id { get; set; }
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime? CapturedAt { get; set; }
    public string? UploadedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>هل يملك المستخدم الحالي حذفه (حسب حالة المالك وصلاحياته).</summary>
    public bool CanDelete { get; set; }
}

/// <summary>مرفقات مالك واحد: العدد وما يملكه المستخدم من إضافة وحذف وحدود الرفع والتصنيفات المسموحة.</summary>
public class DocumentAttachmentSummaryDto
{
    public int Count { get; set; }
    public bool CanAdd { get; set; }
    public bool CanDelete { get; set; }
    public int MaxCount { get; set; }
    public long MaxBytes { get; set; }
    public string[] Categories { get; set; } = Array.Empty<string>();
}

/// <summary>طلب إضافة مرفق (المحتوى واسم الملف يُمرَّران منفصلين من الرفع).</summary>
public class AddDocumentAttachmentRequest
{
    public string? Category { get; set; }
    public string? Note { get; set; }
    /// <summary>وقت الالتقاط من الجهاز (UTC)، اختياري.</summary>
    public DateTime? CapturedAt { get; set; }
}

/// <summary>محتوى مرفق للتنزيل.</summary>
public sealed record DocumentAttachmentFileDto(string FileName, string ContentType, byte[] Content);
