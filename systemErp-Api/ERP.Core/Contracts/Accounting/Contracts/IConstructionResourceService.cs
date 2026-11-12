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

    // ---------- مكتبة تكاليف المقاولات ----------

    /// <summary>ملخص المكتبة: الموارد لكل نوع، وما بلا تكلفة أو ربط أو ملكية، والأسعار المجدولة، والفئات المستعملة.</summary>
    Task<ConstructionCostLibrarySummaryDto> CostSummaryAsync(CancellationToken ct = default);

    /// <summary>سجل تكلفة المورد من الأحدث، مع الساري اليوم والمجدول.</summary>
    Task<ConstructionResourceCostHistoryDto> CostHistoryAsync(Guid resourceId, CancellationToken ct = default);

    /// <summary>يضيف سعراً بتاريخ سريان (ماضٍ أو مستقبلي) لمورد يدوي أو بتفصيل؛ لا يعدّل سطراً قائماً.</summary>
    Task<ConstructionResourceCostHistoryDto> AddCostAsync(Guid resourceId, AddResourceCostDto request, CancellationToken ct = default);

    /// <summary>التكلفة السارية لكل مورد في تاريخ (المصدر الواحد لتسعير الدراسات ونسخها المجمّدة).</summary>
    Task<List<ResourceCostAtDto>> CostsAtAsync(IReadOnlyCollection<Guid> resourceIds, DateTime date, CancellationToken ct = default);
}
