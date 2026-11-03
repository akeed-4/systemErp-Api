using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IApprovalService
{
    /// <summary>يقيّم السياسات الفعّالة، وينشئ طلب اعتماد متعدد المستويات إن انطبقت سياسة.</summary>
    Task<ApprovalCheckResultDto> CheckAndCreateAsync(ApprovalCheckRequestDto request, CancellationToken ct = default);
    /// <summary>هل توجد سياسة فعّالة لعملية المستند (أياً كان حدّ مبلغها)؛ فحص سريع قبل تجربة العملية.</summary>
    Task<bool> HasActivePolicyAsync(string documentType, string actionType, CancellationToken ct = default);
    /// <summary>
    /// يحجز عملية مستند بطلب اعتماد يحمل بياناتها إن انطبقت سياسة بمبلغها؛ وإلا null. داخلي للسرفر (IApprovalGate)
    /// ولا يُكشف عبر approvals/check حتى لا تُحقن عملية ينفّذها المعتمِد.
    /// </summary>
    Task<ApprovalRequestDto?> HoldAsync(ApprovalCheckRequestDto request, string handler, string payload, CancellationToken ct = default);
    /// <summary>العملية المحجوزة في الطلب (للتنفيذ عند الاعتماد النهائي)؛ null للطلبات غير المحجوزة.</summary>
    Task<ApprovalDeferredOperation?> GetDeferredAsync(Guid id, CancellationToken ct = default);
    /// <summary>بعد تنفيذ العملية المحجوزة: ربط الطلب بالمستند الناتج ورقمه.</summary>
    Task CompleteDeferredAsync(Guid id, Guid documentId, string? documentNumber, CancellationToken ct = default);
    Task<PagedResult<ApprovalRequestDto>> ListRequestsAsync(string? status, PaginationParams query, CancellationToken ct = default);
    Task<ApprovalRequestDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<ApprovalRequestDto> GetForApproverAsync(Guid id, CancellationToken ct = default);
    Task<ApprovalRequestDto> ApproveAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
    /// <summary>سحب طلب معلّق (مقدّم الطلب أو المالك/المدير) - يُحفظ في السجل بحالة cancelled ولا يُحذف فيزيائياً.</summary>
    Task<ApprovalRequestDto> CancelAsync(Guid id, CancellationToken ct = default);
    Task<ApprovalRequestDto> RejectAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
}
