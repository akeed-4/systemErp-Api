
namespace ERP.Core.Models.Shared;

public class ApprovalPolicyStep : BaseEntity
{
    public Guid PolicyId { get; set; }
    public int Level { get; set; }
    public string? ApproverRole { get; set; }
    public Guid? ApproverUserId { get; set; }
    public string TitleAr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    /// <summary>مهلة قرار هذا المستوى بالساعات؛ فارغة = بلا مهلة.</summary>
    public int? SlaHours { get; set; }

    public virtual ApprovalPolicy Policy { get; set; } = null!;
}
