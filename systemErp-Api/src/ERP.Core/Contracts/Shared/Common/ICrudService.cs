using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICrudService<TDto, TCreate, TUpdate>
{
    Task<PagedResult<TDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<TDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TDto> CreateAsync(TCreate dto, CancellationToken ct = default);
    Task<TDto> UpdateAsync(Guid id, TUpdate dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
