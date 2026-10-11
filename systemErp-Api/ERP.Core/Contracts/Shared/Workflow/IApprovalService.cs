using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IApprovalService
{
    /// <summary>يقيّم السياسات الفعّالة، وينشئ طلب اعتماد متعدد المستويات إن انطبقت سياسة.</summary>
    Task<ApprovalCheckResultDto> CheckAndCreateAsync(ApprovalCheckRequestDto request, CancellationToken ct = default);
    Task<PagedResult<ApprovalRequestDto>> ListRequestsAsync(string? status, PaginationParams query, CancellationToken ct = default);
    Task<ApprovalRequestDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ApprovalRequestDto> ApproveAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
    /// <summary>سحب طلب معلّق (مقدّم الطلب أو المالك/المدير) - يُحفظ في السجل بحالة cancelled ولا يُحذف فيزيائياً.</summary>
    Task<ApprovalRequestDto> CancelAsync(Guid id, CancellationToken ct = default);
    Task<ApprovalRequestDto> RejectAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
}
