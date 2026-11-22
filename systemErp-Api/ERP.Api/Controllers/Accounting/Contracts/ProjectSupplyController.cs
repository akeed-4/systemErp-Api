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

    /// <summary>متابعة مواد المشروع لكل صنف (الرصيد في مستودع محدد أو مستودع المشروع)، أو مواد مرحلة تنفيذ واحدة بـ <c>phaseId</c>.</summary>
    [HttpGet("material-tracking"), RequireScreen(Projects, ScreenAction.View)]
    public async Task<IActionResult> Tracking(Guid id, [FromQuery] Guid? warehouseId, [FromQuery] Guid? exceptRequisitionId, [FromQuery] Guid? phaseId,
        CancellationToken ct)
        => Success(await _supply.TrackingAsync(id, warehouseId, exceptRequisitionId, phaseId, ct));

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
/// حجز مخزون المستودع للمشروع وتحويل التخصيص لمشروع آخر (تحت سجل العقد ونطاق الوصول إليه)، وأرقام المتاح والمحجوز لأصنافه.
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}")]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ProjectStockReservationsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.ProjectStockReservationsScreenId;
    private readonly IStockReservationService _reservations;

    public ProjectStockReservationsController(IStockReservationService reservations) => _reservations = reservations;

    /// <summary>الرصيد والمحجوز والمصروف والمتاح وقيد التوريد لأصناف المشروع (والمحجوز له والمتاح له).</summary>
    [HttpGet("stock-availability"), RequireScreen(PermissionCatalog.ConstructionProjectsScreenId, ScreenAction.View)]
    public async Task<IActionResult> Availability(Guid id, [FromQuery] Guid[] productIds, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Success(await _reservations.AvailabilityAsync(productIds, warehouseId, id, ct));

    [HttpGet("stock-reservations"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> List(Guid id, CancellationToken ct) => Success(await _reservations.ListAsync(id, ct));

    [HttpPost("stock-reservations"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Reserve(Guid id, [FromBody] SaveStockReservationDto request, CancellationToken ct)
        => Success(await _reservations.ReserveAsync(id, request, ct), "تم حجز الكمية للمشروع.");

    [HttpPost("stock-reservations/{reservationId:guid}/release"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Release(Guid id, Guid reservationId, [FromBody] ReleaseStockReservationDto request, CancellationToken ct)
        => Success(await _reservations.ReleaseAsync(id, reservationId, request, ct), "تم تحرير الكمية من الحجز.");

    [HttpPost("stock-reservations/{reservationId:guid}/transfer"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> RequestTransfer(Guid id, Guid reservationId, [FromBody] RequestReservationTransferDto request, CancellationToken ct)
        => Success(await _reservations.RequestTransferAsync(id, reservationId, request, ct), "أُرسل طلب تحويل التخصيص للاعتماد.");

    [HttpGet("stock-reservation-transfers"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Transfers(Guid id, [FromQuery] string? status, CancellationToken ct)
        => Success(await _reservations.TransfersAsync(status, id, ct));
}

/// <summary>طلبات تحويل التخصيص لكل المشاريع: قائمة المعلّق للمعتمِد، والاعتماد أو الرفض (لا يعتمد مقدّم الطلب طلبه).</summary>
[Route("api/v1/stock-reservation-transfers")]
[RequireModule(PlatformModules.Construction)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class StockReservationTransfersController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.ProjectStockReservationsScreenId;
    private readonly IStockReservationService _reservations;

    public StockReservationTransfersController(IStockReservationService reservations) => _reservations = reservations;

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) => Success(await _reservations.TransfersAsync(status, null, ct));

    [HttpPost("{transferId:guid}/approve"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid transferId, [FromBody] ReservationTransferDecisionDto request, CancellationToken ct)
        => Success(await _reservations.ApproveTransferAsync(transferId, request, ct), "تم اعتماد تحويل التخصيص.");

    [HttpPost("{transferId:guid}/reject"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Reject(Guid transferId, [FromBody] ReservationTransferDecisionDto request, CancellationToken ct)
        => Success(await _reservations.RejectTransferAsync(transferId, request, ct), "تم رفض طلب التحويل.");
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
