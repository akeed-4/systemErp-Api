using ERP.Core.Models.Shared;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>قيد عام متوازن (يدوي أو من أي وحدة).</summary>
public class GenericPostingRequest
{
    public string? Module { get; set; }
    /// <summary>تعديل مستند مرحّل: يُستبدل هذا القيد في مكانه (نفس رقمه) بدل إنشاء قيد جديد.</summary>
    public Guid? ReplaceEntryId { get; set; }
    public DateTime Date { get; set; } = SaudiTime.Today;
    public string Description { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public List<PostingLine> Lines { get; set; } = new();
}
