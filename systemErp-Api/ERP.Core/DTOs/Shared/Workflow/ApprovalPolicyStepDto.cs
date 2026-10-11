namespace ERP.Core.DTOs.Shared;

public partial class ApprovalPolicyStepDto
{
    public Guid Id { get; set; }
    public int Level { get; set; }
    public string? ApproverRole { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
}
