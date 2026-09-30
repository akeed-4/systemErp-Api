using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// اعتماد الجرد: القرار يُفرض في الخادم (صلاحية الاعتماد، فصل المهام، مستويات سياسة inventory_count إن وُجدت)،
/// والاعتماد النهائي ينشئ حركات التسوية والقيد المحاسبي للفروق ذرّياً.
/// </summary>
public interface IInventoryCountApprovalService
{
    Task<PagedResult<InventoryCountApprovalDto>> ListAsync(InventoryCountScope? scope, PaginationParams query, CancellationToken ct = default);
    Task<InventoryCountApprovalDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>القيد المرحَّل للمستند المعتمد، أو معاينة مطابقة للقيد الذي سينشئه الاعتماد (للمستند المعلّق) دون أي أثر محفوظ.</summary>
    Task<InventoryCountJournalDto> JournalAsync(Guid id, CancellationToken ct = default);
    Task<InventoryCountApprovalDto> ApproveAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
    Task<InventoryCountApprovalDto> RejectAsync(Guid id, ApprovalDecisionDto decision, CancellationToken ct = default);
}
