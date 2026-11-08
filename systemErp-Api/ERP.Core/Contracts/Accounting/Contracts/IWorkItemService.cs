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
    /// <summary>ينشئ صنفاً خدمياً من البند (أو يحدّث المرتبط به) برمزه واسمه ووحدته وسعره وتكلفته، ويربطه به.</summary>
    Task<WorkItemDto> SyncServiceItemAsync(Guid id, CancellationToken ct = default);
    /// <summary>
    /// ينشئ بنداً من صنف قائم: الاسم والوحدة وسعر البيع منه، ومكوناته (أو الصنف نفسه إن كان بضاعة) احتياجات بموارد المكتبة
    /// المربوطة بها، ويُنشأ المورد الناقص. الصنف الخدمي يُربط بالبند.
    /// </summary>
    Task<WorkItemDto> CreateFromProductAsync(Guid productId, CancellationToken ct = default);
    /// <summary>بنود المكتبة التي تستخدم الصنف (صنفها الخدمي، أو مادة مربوطة به في احتياجاتها) لبطاقة الصنف.</summary>
    Task<List<WorkItemProductUsageDto>> ProductUsageAsync(Guid productId, CancellationToken ct = default);
}
