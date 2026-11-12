using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>موارد مكتبة المقاولات (مادة/عمالة/معدة...) بتكلفتها المعيارية؛ شاشة «مكتبة المقاولات».</summary>
[Route("api/v1/construction-resources"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId)]
[RequireModule(PlatformModules.Construction)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ConstructionResourcesController : CrudController<ConstructionResourceDto, CreateConstructionResourceDto, UpdateConstructionResourceDto>
{
    private readonly IConstructionResourceService _resources;

    public ConstructionResourcesController(IConstructionResourceService s) : base(s) => _resources = s;

    /// <summary>إعادة حساب تكاليف الموارد غير اليدوية من مصادرها (الأصناف، وشؤون الموظفين لمن يملك صلاحيتها).</summary>
    [HttpPost("refresh-costs"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId, ScreenAction.Edit)]
    public async Task<IActionResult> RefreshCosts(CancellationToken ct)
    {
        var result = await _resources.RefreshCostsAsync(ct);
        return Success(result, string.Format(Messages.ResourceCostsRefreshed, result.Updated));
    }

    /// <summary>المواد غير المربوطة بصنف ومرشحوها للربط التلقائي.</summary>
    [HttpGet("link-suggestions"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId, ScreenAction.View)]
    public async Task<IActionResult> LinkSuggestions(CancellationToken ct) => Success(await _resources.LinkSuggestionsAsync(ct));

    /// <summary>ربط مواد بأصناف دفعة واحدة (بعد مراجعة المستخدم للمرشحين).</summary>
    [HttpPost("link-products"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId, ScreenAction.Edit)]
    public async Task<IActionResult> LinkProducts([FromBody] LinkResourcesToProductsDto request, CancellationToken ct)
    {
        var result = await _resources.LinkProductsAsync(request, ct);
        return Success(result, $"تم ربط {result.Linked} مادة بأصنافها.");
    }

    /// <summary>المهن والأقسام من شؤون الموظفين لربط مورد العمالة (أعداد فقط)؛ الخدمة تشترط صلاحية شؤون الموظفين.</summary>
    [HttpGet("hr-labor-options"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId, ScreenAction.View), RequireModule(PlatformModules.Hr)]
    public async Task<IActionResult> HrLaborOptions(CancellationToken ct)
        => Success(await _resources.HrLaborOptionsAsync(ct));

    // ---------- مكتبة تكاليف المقاولات ----------

    private const string CostLibrary = PermissionCatalog.ConstructionCostLibraryScreenId;

    /// <summary>ملخص المكتبة: الموارد لكل نوع وما ينقصها، والأسعار المجدولة، والفئات المستعملة.</summary>
    [HttpGet("cost-summary"), RequireScreen(CostLibrary, ScreenAction.View)]
    public async Task<IActionResult> CostSummary(CancellationToken ct) => Success(await _resources.CostSummaryAsync(ct));

    /// <summary>سجل تكلفة المورد (للقراءة فقط؛ لا تعديل ولا حذف لأسطره).</summary>
    [HttpGet("{id:guid}/costs"), RequireScreen(CostLibrary, ScreenAction.View)]
    public async Task<IActionResult> CostHistory(Guid id, CancellationToken ct) => Success(await _resources.CostHistoryAsync(id, ct));

    /// <summary>سعر جديد بتاريخ سريان (ماضٍ أو مستقبلي) لمورد يدوي أو بتفصيل.</summary>
    [HttpPost("{id:guid}/costs"), RequireScreen(CostLibrary, ScreenAction.Edit)]
    public async Task<IActionResult> AddCost(Guid id, [FromBody] AddResourceCostDto request, CancellationToken ct)
        => Success(await _resources.AddCostAsync(id, request, ct));

    /// <summary>التكلفة السارية في تاريخ لمجموعة موارد (<c>?date=2026-11-01&amp;ids=..&amp;ids=..</c>).</summary>
    [HttpGet("costs-at"), RequireScreen(CostLibrary, ScreenAction.View)]
    public async Task<IActionResult> CostsAt([FromQuery] DateTime? date, [FromQuery] List<Guid> ids, CancellationToken ct)
        => Success(await _resources.CostsAtAsync(ids, date ?? DateTime.UtcNow.Date, ct));
}
