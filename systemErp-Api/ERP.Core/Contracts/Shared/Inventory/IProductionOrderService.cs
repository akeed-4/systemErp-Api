using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>مستندات تحويل وتصنيع المنتجات: مسودة ← مرحّل (حركات المواد والمنتج + القيد) ← ملغى (عكس كامل).</summary>
public interface IProductionOrderService
{
    Task<PagedResult<ProductionOrderDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<ProductionOrderDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>أسطر مقترحة من مكونات المنتج بكمية منتجة (معاينة لا تُحفظ) مع الأرصدة الحالية.</summary>
    Task<List<ProductionOrderLineDto>> TemplateAsync(Guid productId, decimal quantity, Guid? materialsWarehouseId, CancellationToken ct = default);
    Task<ProductionOrderDto> CreateAsync(SaveProductionOrderDto request, CancellationToken ct = default);
    Task<ProductionOrderDto> UpdateAsync(Guid id, SaveProductionOrderDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ProductionOrderDto> PostAsync(Guid id, CancellationToken ct = default);
    Task<ProductionOrderDto> CancelAsync(Guid id, string reason, CancellationToken ct = default);
}
