namespace ERP.Core.Models.Accounting;

/// <summary>A generated compliance-only invoice document and its durable ZATCA submission result.</summary>
public class ZatcaComplianceTest : BaseEntity
{
    public Guid SessionId { get; set; }
    public string DocumentKey { get; set; } = string.Empty;
    public bool IsSimplified { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string Status { get; set; } = "generated";
    public Guid Uuid { get; set; }
    public string InvoiceHash { get; set; } = string.Empty;
    public string SignedXml { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int? HttpStatus { get; set; }
    public string? ResponseMessage { get; set; }
    public string? ErrorsJson { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
