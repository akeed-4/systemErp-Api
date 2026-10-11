namespace ERP.Core.DTOs.Shared;

public partial class CreateApprovalPolicyDto
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public decimal? MinAmountTrigger { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public List<ApprovalPolicyStepDto> Steps { get; set; } = new();
}
