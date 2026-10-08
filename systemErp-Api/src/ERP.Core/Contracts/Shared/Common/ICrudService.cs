using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICrudService<TDto, TCreate, TUpdate>
{
    Task<PagedResult<TDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    /// <summary>القائمة بخيارات DevExtreme (filter/sort/skip/take/totalSummary) منفَّذة في قاعدة البيانات؛ الصفوف بنفس DTO القائمة.</summary>
    Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<TDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TDto> CreateAsync(TCreate dto, CancellationToken ct = default);
    Task<TDto> UpdateAsync(Guid id, TUpdate dto, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
