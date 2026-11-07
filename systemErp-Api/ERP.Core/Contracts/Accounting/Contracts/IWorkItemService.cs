using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>مكتبة البنود: البند الأساسي ببنوده الفرعية واحتياجاته من الموارد، وتكلفته وسعره وهامشه (يحسبها الخادم).</summary>
public interface IWorkItemService
{
    Task<List<WorkItemSummaryDto>> ListAsync(CancellationToken ct = default);
    Task<WorkItemDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<WorkItemDto> CreateAsync(SaveWorkItemDto request, CancellationToken ct = default);
    Task<WorkItemDto> UpdateAsync(Guid id, SaveWorkItemDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
