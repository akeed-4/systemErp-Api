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
[RequireModule(PlatformModules.Construction)]
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

    /// <summary>مواد كل بند في جدول الكميات (المخطط، المسلَّم وتكلفته، المختار في المستخلصات، المتبقي تحت التنفيذ).</summary>
    [HttpGet("material-tracking/boq"), RequireScreen(Projects, ScreenAction.View)]
    public async Task<IActionResult> BoqMaterials(Guid id, CancellationToken ct) => Success(await _supply.BoqMaterialsAsync(id, ct));

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

/// <summary>
/// أذونات تسليم المواد لكل المشاريع (شاشة المستندات المستقلة في قائمة المقاولات): القائمة بترقيم وفلترة من السرفر، في نطاق وصول
/// المستخدم إلى العقود. فتح الإذن وحفظه وترحيله وإلغاؤه تبقى على مسار سجل العقد (`commercialcontracts/{id}/material-issues`).
/// </summary>
[Route("api/v1/project-material-issues")]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ProjectMaterialIssuesIndexController : ErpControllerBase
{
    private readonly IProjectSupplyService _supply;

    public ProjectMaterialIssuesIndexController(IProjectSupplyService supply) => _supply = supply;

    [HttpGet("load"), RequireScreen(PermissionCatalog.ProjectMaterialIssuesScreenId, ScreenAction.View)]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _supply.LoadIssuesAsync(loadOptions, ct));
}
