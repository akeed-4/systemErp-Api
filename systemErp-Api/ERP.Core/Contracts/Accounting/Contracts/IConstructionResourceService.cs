using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>موارد مكتبة المقاولات: تعريفها وتكلفتها المعيارية، وتحديث تكاليف المرتبطة بأصنافها.</summary>
public interface IConstructionResourceService : ICrudService<ConstructionResourceDto, CreateConstructionResourceDto, UpdateConstructionResourceDto>
{
    /// <summary>يعيد حساب تكلفة كل مورد غير يدوي من مصدره الحالي (الصنف المخزني أو شؤون الموظفين).</summary>
    Task<ResourceCostRefreshDto> RefreshCostsAsync(CancellationToken ct = default);

    /// <summary>المهن والأقسام من شؤون الموظفين لربط مورد العمالة (أعداد فقط، بلا رواتب).</summary>
    Task<HrLaborOptionsDto> HrLaborOptionsAsync(CancellationToken ct = default);

    /// <summary>المواد غير المربوطة بصنف مخزني ومرشحوها من الأصناف (بالرمز، ثم الاسم المطابق، ثم المتشابه).</summary>
    Task<List<ResourceLinkSuggestionDto>> LinkSuggestionsAsync(CancellationToken ct = default);

    /// <summary>يربط مواد بأصناف دفعة واحدة: تكلفتها تصبح من الصنف. يتخطى غير المتوافق بسببه.</summary>
    Task<ResourceLinkResultDto> LinkProductsAsync(LinkResourcesToProductsDto request, CancellationToken ct = default);
}
