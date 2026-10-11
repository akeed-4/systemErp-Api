using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// إهلاك الأصول الثابتة بالفترة الشهرية: قسط ثابت من تكلفة الشراء بالنسبة السنوية. الترحيل ذرّي عبر المحرك المحاسبي،
/// ومركز تكلفة الأصل يُحمَّل على سطري القيد، ولا يُرحَّل الأصل مرتين لنفس الفترة.
/// </summary>
public interface IFixedAssetDepreciationService
{
    /// <summary>يحسب إهلاك الفترة ويبيّن حالة كل أصل دون أي أثر محفوظ.</summary>
    Task<DepreciationRunDto> PreviewAsync(DepreciationRunRequestDto request, CancellationToken ct = default);
    Task<DepreciationRunDto> PostAsync(DepreciationRunRequestDto request, CancellationToken ct = default);
    /// <summary>يعكس ترحيل إهلاك بقيده: يعيد أرصدة الأصول ويحذف سجلات الفترة (آخر فترة لكل أصل فقط).</summary>
    Task ReverseRunAsync(Guid journalEntryId, CancellationToken ct = default);
    Task<PagedResult<FixedAssetDepreciationDto>> ListAsync(Guid? fixedAssetId, Guid? costCenterId, Guid? warehouseId, string? period, PaginationParams query, CancellationToken ct = default);
}
