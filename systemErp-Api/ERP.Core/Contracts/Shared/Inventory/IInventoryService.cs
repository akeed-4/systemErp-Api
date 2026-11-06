using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IInventoryService
{
    /// <summary>يسجّل حركة مخزون ويحدّث رصيد الصنف وتكلفته وفق سياسة التكلفة. يُستدعى من المبيعات/المشتريات/POS/الجرد.</summary>
    Task<StockMovementDto> RecordMovementAsync(RecordStockMovementDto request, CancellationToken ct = default);
    /// <summary>تعديل تسوية يدوية (SourceType=manual): يُعاد احتساب رصيد الصنف وتكلفته.</summary>
    Task<StockMovementDto> UpdateMovementAsync(Guid id, RecordStockMovementDto request, CancellationToken ct = default);
    Task DeleteMovementAsync(Guid id, CancellationToken ct = default);
    Task<StockMovementDto> GetMovementAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<StockMovementDto>> ListMovementsAsync(Guid? itemId, Guid? warehouseId, PaginationParams query, CancellationToken ct = default);
    /// <summary>ينقل كمية صنف بين مستودعين: رصيد الصنف الإجمالي وتكلفته لا يتغيّران. يُرجع التشغيلات المنقولة (للصنف المتتبَّع).</summary>
    Task<List<BatchAllocationDto>> TransferAsync(TransferStockDto request, CancellationToken ct = default);
    /// <summary>أرصدة الأصناف في المستودعات (غير الصفرية)، لصنف أو مستودع محدَّد أو للكل.</summary>
    Task<List<WarehouseStockDto>> GetWarehouseStockAsync(Guid? itemId, Guid? warehouseId, CancellationToken ct = default);
    /// <summary>تشغيلات صنف بأرصدتها (في مستودع أو في الكل) مرتبة بالأقرب انتهاءً؛ includeEmpty يشمل المستنفدة.</summary>
    Task<List<ItemBatchDto>> GetBatchesAsync(Guid itemId, Guid? warehouseId, bool includeEmpty, CancellationToken ct = default);
    /// <summary>تعديل تاريخي تشغيلة وملاحظاتها (الرقم ثابت).</summary>
    Task<ItemBatchDto> UpdateBatchAsync(Guid batchId, UpdateItemBatchDto request, CancellationToken ct = default);
    /// <summary>تقرير الصلاحية: أرصدة التشغيلات (غير الصفرية) بحالتها. status: expired | near_expiry | valid | no_expiry | فارغ = الكل.</summary>
    Task<List<BatchExpiryRowDto>> GetExpiryReportAsync(Guid? warehouseId, string? status, int? withinDays, CancellationToken ct = default);
}
