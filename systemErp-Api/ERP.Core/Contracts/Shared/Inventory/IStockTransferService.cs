using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>مستند تحويل المخزون بين المستودعات: ينقل الكميات دون أثر على التكلفة أو الدفاتر.</summary>
public interface IStockTransferService
{
    Task<StockTransferDto> CreateAsync(CreateStockTransferDto request, CancellationToken ct = default);
    Task<StockTransferDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<StockTransferDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
}
