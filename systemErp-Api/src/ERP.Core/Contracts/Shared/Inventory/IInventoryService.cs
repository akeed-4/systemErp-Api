using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IInventoryService
{
    /// <summary>يسجّل حركة مخزون ويحدّث رصيد الصنف وتكلفته وفق سياسة التكلفة. يُستدعى من المبيعات/المشتريات/POS/الجرد.</summary>
    Task<StockMovementDto> RecordMovementAsync(RecordStockMovementDto request, CancellationToken ct = default);
    /// <summary>تعديل تسوية يدوية (SourceType=manual): يُعاد احتساب رصيد الصنف وتكلفته.</summary>
    Task<StockMovementDto> UpdateMovementAsync(Guid id, RecordStockMovementDto request, CancellationToken ct = default);
    Task DeleteMovementAsync(Guid id, CancellationToken ct = default);
    /// <summary>يسحب كل حركات مستند (عند إلغاء ترحيله) ويعيد احتساب الأصناف المتأثرة.</summary>
    Task RemoveDocumentMovementsAsync(string sourceType, Guid sourceId, CancellationToken ct = default);
    Task<StockMovementDto> GetMovementAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid? itemId, PaginationParams query, CancellationToken ct = default);
}
