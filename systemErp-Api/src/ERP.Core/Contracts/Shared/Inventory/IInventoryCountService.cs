using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>مستندات الجرد (أصناف ومركبات): الإدخال والعدّ حتى الإرسال للاعتماد. التسوية عبر IInventoryCountApprovalService فقط.</summary>
public interface IInventoryCountService : ICrudService<InventoryCountDto, CreateInventoryCountDto, UpdateInventoryCountDto>
{
    /// <summary>القائمة مع تصفية النطاق (items | vehicles) والحالة.</summary>
    Task<PagedResult<InventoryCountDto>> ListAsync(InventoryCountScope? scope, PaginationParams query, CancellationToken ct = default);
    /// <summary>أسطر الجرد المتوقعة من رصيد النظام الحالي (دون حفظ).</summary>
    Task<List<InventoryCountLineDto>> SnapshotAsync(InventoryCountSnapshotRequestDto request, CancellationToken ct = default);
    /// <summary>يجمّد رصيد النظام وينشئ مستند اعتماد جديد. يتطلب عدّ كل الأسطر.</summary>
    Task<InventoryCountApprovalDto> SubmitAsync(Guid id, CancellationToken ct = default);
    /// <summary>سحب الإرسال المعلّق (مقدّمه أو المالك/المدير) وإعادة الجرد مسودة.</summary>
    Task<InventoryCountDto> WithdrawAsync(Guid id, CancellationToken ct = default);
    /// <summary>إلغاء جرد لم يُعتمد (مسودة أو مرفوض) مع إبقائه في السجل.</summary>
    Task<InventoryCountDto> CancelAsync(Guid id, CancellationToken ct = default);
    /// <summary>مستندات الاعتماد لهذا الجرد (كل جولات الإرسال).</summary>
    Task<List<InventoryCountApprovalDto>> ApprovalsAsync(Guid id, CancellationToken ct = default);
}
