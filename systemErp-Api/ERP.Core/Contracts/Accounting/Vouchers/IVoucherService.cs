using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IVoucherService
{
    Task<PagedResult<VoucherDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<VoucherDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>ينشئ السند ويرحّل قيده في معاملة واحدة.</summary>
    Task<VoucherDto> CreateAsync(CreateVoucherDto request, CancellationToken ct = default);
    /// <summary>يعكس قيد السند القديم ويرحّل الجديد بنفس الرقم.</summary>
    Task<VoucherDto> UpdateAsync(Guid id, UpdateVoucherDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
