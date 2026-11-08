using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// توريد مواد المشروع تحت سجل العقد (يخضع لنطاق الوصول على العقد): متابعة المطلوب/المطلوب شراؤه/المشترى/المسلَّم/المتبقي،
/// ومستندات صرف مواد المستودع للمشروع.
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}")]
[RequireModule(PlatformModules.Accounting)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ProjectSupplyController : ErpControllerBase
{
    private const string Projects = PermissionCatalog.ConstructionProjectsScreenId;
    private const string Issues = PermissionCatalog.ProjectMaterialIssuesScreenId;
    private readonly IProjectSupplyService _supply;

    public ProjectSupplyController(IProjectSupplyService supply) => _supply = supply;

    /// <summary>متابعة مواد المشروع لكل صنف (الرصيد في مستودع محدد أو مستودع المشروع).</summary>
    [HttpGet("material-tracking"), RequireScreen(Projects, ScreenAction.View)]
    public async Task<IActionResult> Tracking(Guid id, [FromQuery] Guid? warehouseId, [FromQuery] Guid? exceptRequisitionId, CancellationToken ct)
        => Success(await _supply.TrackingAsync(id, warehouseId, exceptRequisitionId, ct));

    [HttpGet("material-issues"), RequireScreen(Issues, ScreenAction.View)]
    public async Task<IActionResult> List(Guid id, CancellationToken ct) => Success(await _supply.ListIssuesAsync(id, ct));

    [HttpGet("material-issues/{issueId:guid}"), RequireScreen(Issues, ScreenAction.View)]
    public async Task<IActionResult> Get(Guid id, Guid issueId, CancellationToken ct) => Success(await _supply.GetIssueAsync(id, issueId, ct));

    [HttpPost("material-issues"), RequireScreen(Issues, ScreenAction.Create)]
    public async Task<IActionResult> Create(Guid id, [FromBody] SaveProjectMaterialIssueDto request, [FromServices] IPermissionService permissions, CancellationToken ct)
    {
        if (request.Post && !await permissions.HasPermissionAsync(Issues, ScreenAction.Post, ct)) throw new ForbiddenException();
        return Success(await _supply.CreateIssueAsync(id, request, ct));
    }

    [HttpPut("material-issues/{issueId:guid}"), RequireScreen(Issues, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, Guid issueId, [FromBody] SaveProjectMaterialIssueDto request, [FromServices] IPermissionService permissions, CancellationToken ct)
    {
        if (request.Post && !await permissions.HasPermissionAsync(Issues, ScreenAction.Post, ct)) throw new ForbiddenException();
        return Success(await _supply.UpdateIssueAsync(id, issueId, request, ct));
    }

    [HttpDelete("material-issues/{issueId:guid}"), RequireScreen(Issues, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid issueId, CancellationToken ct)
    {
        await _supply.DeleteIssueAsync(id, issueId, ct);
        return Success(true);
    }

    [HttpPost("material-issues/{issueId:guid}/post"), RequireScreen(Issues, ScreenAction.Post)]
    public async Task<IActionResult> Post(Guid id, Guid issueId, CancellationToken ct)
        => Success(await _supply.PostIssueAsync(id, issueId, ct), "تم ترحيل صرف المواد للمشروع.");

    [HttpPost("material-issues/{issueId:guid}/cancel"), RequireScreen(Issues, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, Guid issueId, [FromBody] CancelProductionOrderDto request, CancellationToken ct)
        => Success(await _supply.CancelIssueAsync(id, issueId, request.Reason, ct), "تم إلغاء صرف المواد وعكس أثره.");
}
