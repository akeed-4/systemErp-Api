using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>سجل دراسات الجدوى (في نطاق وصول المستخدم إلى العقود): الدراسة هي سجل العقد نفسه قبل «إنشاء العقد».</summary>
[Route("api/v1/feasibility-studies")]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class FeasibilityStudiesController : ErpControllerBase
{
    private readonly IFeasibilityStudyService _studies;

    public FeasibilityStudiesController(IFeasibilityStudyService studies) => _studies = studies;

    [HttpGet, RequireScreen(PermissionCatalog.CommercialContractsScreenId, ScreenAction.View)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) => Success(await _studies.ListAsync(status, ct));

    /// <summary>«إنشاء العقد» من الدراسة والخطة المعتمدتين (صلاحية إنشاء العقود).</summary>
    [HttpPost("{id:guid}/create-contract"), RequireScreen(PermissionCatalog.CommercialContractsScreenId, ScreenAction.Create)]
    public async Task<IActionResult> CreateContract(Guid id, [FromBody] CreateContractFromStudyDto request, CancellationToken ct)
        => Success(await _studies.CreateContractAsync(id, request, ct));

    /// <summary>تفعيل عقد مُنشأ من دراسة وبقي مسودة (صلاحية الاعتماد تُفحص في الخدمة بنطاقها وحدها).</summary>
    [HttpPost("{id:guid}/activate-contract"), RequireScreen(PermissionCatalog.CommercialContractsScreenId, ScreenAction.Approve)]
    public async Task<IActionResult> ActivateContract(Guid id, CancellationToken ct)
        => Success(await _studies.ActivateContractAsync(id, ct));
}
