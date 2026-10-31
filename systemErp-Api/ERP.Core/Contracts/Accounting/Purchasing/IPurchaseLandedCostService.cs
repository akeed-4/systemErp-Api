using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>أنواع التكاليف الإضافية للمشتريات العامة (كتالوج مستقل عن مصاريف السيارات).</summary>
public interface IPurchaseExpenseTypeService
{
    Task<List<PurchaseExpenseTypeDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<PurchaseExpenseTypeDto> CreateAsync(SavePurchaseExpenseTypeDto dto, CancellationToken ct = default);
    Task<PurchaseExpenseTypeDto> UpdateAsync(Guid id, SavePurchaseExpenseTypeDto dto, CancellationToken ct = default);
}

/// <summary>سند التكاليف الإضافية للمشتريات العامة: تحميل الشحن والجمارك والتخليص على تكلفة أصناف فواتير الشراء.</summary>
public interface IPurchaseLandedCostService
{
    Task<PagedResult<PurchaseLandedCostDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<PurchaseLandedCostDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>يحسب التوزيع وأثره على تكلفة الأصناف دون حفظ.</summary>
    Task<PurchaseLandedCostDto> PreviewAsync(SavePurchaseLandedCostDto dto, CancellationToken ct = default);
    Task<PurchaseLandedCostDto> CreateAsync(SavePurchaseLandedCostDto dto, CancellationToken ct = default);
    Task<PurchaseLandedCostDto> UpdateAsync(Guid id, SavePurchaseLandedCostDto dto, CancellationToken ct = default);
    /// <summary>يرفع تكلفة حركات الشراء بالحصص ويرحّل القيد: المخزون بتغيّر قيمته، والباقي تكلفة مبيعات أو فرق سعر.</summary>
    Task<PurchaseLandedCostDto> ApproveAsync(Guid id, CancellationToken ct = default);
    /// <summary>مسودة: تُلغى. معتمد: تُعاد التكلفة ويُرحَّل قيد معاكس محسوب بالطريقة نفسها.</summary>
    Task<PurchaseLandedCostDto> CancelAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
