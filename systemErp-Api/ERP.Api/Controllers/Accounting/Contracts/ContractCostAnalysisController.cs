using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// تحليل تكلفة دراسة الجدوى (جدول الكميات) من مكتبة التكاليف، مجمّداً على الأسطر. القراءة بعرض العقد، والتسعير والتحديث بتعديله،
/// والتعديل اليدوي للتكلفة بتعديل العقد وتعديل مكتبة التكاليف معاً.
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}/cost-analysis"), RequireScreen(PermissionCatalog.CommercialContractsScreenId)]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ContractCostAnalysisController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.CommercialContractsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;
    private readonly IContractCostAnalysisService _analysis;
    private readonly IPermissionService _permissions;

    public ContractCostAnalysisController(IContractCostAnalysisService analysis, IPermissionService permissions)
    {
        _analysis = analysis; _permissions = permissions;
    }

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _analysis.GetAsync(id, ct));

    /// <summary>التسعير من المكتبة في تاريخ بالفروق لكل سطر (لا يُحفظ).</summary>
    [HttpGet("preview"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] DateTime? date, CancellationToken ct)
        => Success(await _analysis.PreviewAsync(id, date, ct));

    /// <summary>تطبيق التسعير على الأسطر المختارة (أو كلها).</summary>
    [HttpPost("apply"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Apply(Guid id, [FromBody] ApplyContractCostRefreshDto request, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return Success(await _analysis.ApplyAsync(id, request, ct));
    }

    /// <summary>تعديل تكلفة وحدة مورد في الدراسة بسبب.</summary>
    [HttpPut("{rowId:guid}"), RequireScreen(PermissionCatalog.ConstructionCostLibraryScreenId, ScreenAction.Edit)]
    public async Task<IActionResult> Override(Guid id, Guid rowId, [FromBody] OverrideContractCostDto request, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return Success(await _analysis.OverrideAsync(id, rowId, request, ct));
    }
}
