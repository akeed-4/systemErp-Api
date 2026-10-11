namespace ERP.Core.DTOs.Shared;

public class ApprovalCheckRequestDto
{
    /// <summary>sales | purchase | receipt_voucher | payment_voucher | sales_return | ...</summary>
    public string DocumentType { get; set; } = string.Empty;
    /// <summary>create | edit | delete | post | ...</summary>
    public string ActionType { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal DocumentAmount { get; set; }
    public string? DocumentDataSnapshot { get; set; }
}
