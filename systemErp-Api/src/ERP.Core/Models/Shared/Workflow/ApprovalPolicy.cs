
namespace ERP.Core.Models.Shared;

/// <summary>
/// سياسة اعتماد متعددة المستويات لنوع مستند معين، تُستخدم بواسطة <see cref="ApprovalRequest"/>
/// لتحديد هل يلزم مسار اعتماد للمستند، ومن هم المعتمدون في كل مستوى.
/// </summary>
public class ApprovalPolicy : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public decimal? MinAmountTrigger { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public virtual ICollection<ApprovalPolicyStep> Steps { get; set; } = new List<ApprovalPolicyStep>();
}
