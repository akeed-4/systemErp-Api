
namespace ERP.Core.Models.Shared;

public class ApprovalPolicyStep : BaseEntity
{
    public Guid PolicyId { get; set; }
    public int Level { get; set; }
    public string? ApproverRole { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;

    public virtual ApprovalPolicy Policy { get; set; } = null!;
}
