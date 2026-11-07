using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>موارد مكتبة المقاولات (مادة/عمالة/معدة...) بتكلفتها المعيارية؛ شاشة «مكتبة المقاولات».</summary>
[Route("api/v1/construction-resources"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId)]
[RequireModule(PlatformModules.Accounting)]
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

    /// <summary>المهن والأقسام من شؤون الموظفين لربط مورد العمالة (أعداد فقط)؛ الخدمة تشترط صلاحية شؤون الموظفين.</summary>
    [HttpGet("hr-labor-options"), RequireScreen(PermissionCatalog.ConstructionLibraryScreenId, ScreenAction.View), RequireModule(PlatformModules.Hr)]
    public async Task<IActionResult> HrLaborOptions(CancellationToken ct)
        => Success(await _resources.HrLaborOptionsAsync(ct));
}
