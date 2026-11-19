using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>متابعة اعتماد المستندات من شاشاتها (السجلات يكتبها خطّاف الحفظ تلقائياً).</summary>
public interface IApprovalTrackingService
{
    /// <summary>
    /// <paramref name="scope"/>: mine = ما قدّمه المستخدم، awaiting = المعلّق على شاشات يملك اعتمادها.
    /// <paramref name="status"/>: pending/approved/rejected/returned/cancelled أو فارغ للكل.
    /// </summary>
    Task<PagedResult<ApprovalTrackItemDto>> ListAsync(string scope, string? status, PaginationParams p, CancellationToken ct = default);
    Task<ApprovalTrackSummaryDto> SummaryAsync(CancellationToken ct = default);
    /// <summary>رسالة «تم الحفظ وأُرسل للاعتماد إلى...» لما قدّمه المستخدم في الطلب الحالي دون أن يملك اعتماده، أو null.</summary>
    Task<string?> SubmissionMessageAsync(CancellationToken ct = default);
}
