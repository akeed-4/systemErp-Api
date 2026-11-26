using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// فواتير الصرف والاستلام المخزني (kind = issue | receipt): مسودة ← مرحّل (حركات المخزون + القيد) ← ملغى (عكس كامل).
/// كل عملية تُقيَّد بنوع المستند: مستند من نوع آخر بالمعرّف نفسه غير موجود لهذه الشاشة.
/// </summary>
public interface IInventoryVoucherService
{
    Task<PagedResult<InventoryVoucherDto>> ListAsync(string kind, PaginationParams query, CancellationToken ct = default);
    Task<InventoryVoucherDto> GetAsync(string kind, Guid id, CancellationToken ct = default);
    Task<InventoryVoucherDefaultsDto> DefaultsAsync(string kind, CancellationToken ct = default);
    Task<InventoryVoucherDto> CreateAsync(string kind, SaveInventoryVoucherDto request, CancellationToken ct = default);
    Task<InventoryVoucherDto> UpdateAsync(string kind, Guid id, SaveInventoryVoucherDto request, CancellationToken ct = default);
    Task DeleteAsync(string kind, Guid id, CancellationToken ct = default);
    Task<InventoryVoucherDto> PostAsync(string kind, Guid id, CancellationToken ct = default);
    Task<InventoryVoucherDto> CancelAsync(string kind, Guid id, string reason, CancellationToken ct = default);
}
