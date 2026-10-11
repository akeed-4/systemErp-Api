namespace ERP.Core.DTOs.Shared;

public partial class ApprovalPolicyDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public decimal? MinAmountTrigger { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public List<ApprovalPolicyStepDto> Steps { get; set; } = new();
}
