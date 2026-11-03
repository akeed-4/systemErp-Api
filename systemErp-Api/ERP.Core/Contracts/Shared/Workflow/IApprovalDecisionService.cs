using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// قرار المعتمِد على طلب اعتماد من شاشة الطلب/الإشعار، مع أثره على المستند نفسه: الاعتماد النهائي يرحّل المستند
/// (أو يسوّي الجرد) في معاملة القرار نفسها، والرفض يعيد المستند لمقدّمه مسودةً مرفوضة بسببها.
/// </summary>
public interface IApprovalDecisionService
{
    Task<ApprovalRequestDto> ApproveAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
    Task<ApprovalRequestDto> RejectAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
}
