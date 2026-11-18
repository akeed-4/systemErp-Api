namespace ERP.Core.DTOs.Shared;

public class ApprovalDecisionDto
{
    public string? Comment { get; set; }
    /// <summary>
    /// المستوى الذي يقرّر عليه المستخدم كما رآه؛ إن تغيّر في الأثناء (قرار متزامن أو نقر مكرر) يُرفض القرار بدل أن يُعتمد
    /// المستوى التالي بالخطأ. اختياري للتوافق مع العملاء القدامى.
    /// </summary>
    public int? ExpectedLevel { get; set; }
}
