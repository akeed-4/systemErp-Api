using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.POS;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.POS;

public interface IPosSaleReturnService
{
    Task<PosSalesReturnDto> CreateAsync(CreatePosReturnRequestDto request, CancellationToken ct = default);
    Task<PosSalesReturnDto> UpdateAsync(Guid id, UpdatePosReturnRequestDto request, CancellationToken ct = default);
    /// <summary>حذف مرتجع: يُحذف إشعاره الدائن بعكس أثره ويُعاد المخزون/الوردية/الولاء والعملية الأصلية كما كانت.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<PosSalesReturnDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<PosSalesReturnDto> GetAsync(Guid id, CancellationToken ct = default);
}
