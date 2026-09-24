using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

/// <summary>قيد عام متوازن (يدوي أو من أي وحدة).</summary>
public class GenericPostingRequest
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public List<PostingLine> Lines { get; set; } = new();
}
