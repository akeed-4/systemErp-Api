using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.POS;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.POS;

public interface IPosLoyaltyService
{
    Task<PagedResult<CustomerLoyaltyDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<CustomerLoyaltyDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CustomerLoyaltyDto?> GetByCustomerAsync(Guid customerId, CancellationToken ct = default);
}
