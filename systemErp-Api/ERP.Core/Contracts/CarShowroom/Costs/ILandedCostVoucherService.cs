using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ILandedCostVoucherService
{
    Task<PagedResult<LandedCostVoucherDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<LandedCostVoucherDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>يحسب التوزيع وأثره على تكلفة السيارات دون حفظ.</summary>
    Task<LandedCostPreviewDto> PreviewAsync(CreateLandedCostVoucherDto dto, CancellationToken ct = default);
    Task<LandedCostVoucherDto> CreateAsync(CreateLandedCostVoucherDto dto, CancellationToken ct = default);
    Task<LandedCostVoucherDto> UpdateAsync(Guid id, UpdateLandedCostVoucherDto dto, CancellationToken ct = default);
    /// <summary>يحوّل أسطر السند إلى مصاريف فعلية تحل محل التقديري المقابل وتعدّل تكلفة السيارات بالفرق.</summary>
    Task<LandedCostVoucherDto> ApproveAsync(Guid id, CancellationToken ct = default);
    /// <summary>مسودة: تُلغى. معتمد: يُعكس أثره (تُحذف مصاريفه الفعلية وتعود الأسطر المستبدَلة).</summary>
    Task<LandedCostVoucherDto> CancelAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
