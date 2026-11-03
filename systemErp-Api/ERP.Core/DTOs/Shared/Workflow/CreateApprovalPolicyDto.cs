namespace ERP.Core.DTOs.Shared;

public partial class CreateApprovalPolicyDto
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>مستند واحد أو أكثر من فهرس الاعتماد (approvals/document-types).</summary>
    public List<string> DocumentTypes { get; set; } = new();
    public string ActionType { get; set; } = string.Empty;
    public decimal? MinAmountTrigger { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public List<ApprovalPolicyStepDto> Steps { get; set; } = new();
}
