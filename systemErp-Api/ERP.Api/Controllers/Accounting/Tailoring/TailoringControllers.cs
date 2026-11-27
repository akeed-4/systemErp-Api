using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// إعداد الخياطة (شاشة الإعدادات؛ العرض للقراءة والتعديل لكل كتابة). القوالب ومراحل الإنتاج تقرؤها أيضاً شاشات العملاء والأوامر
/// لإدخال القياس واختيار التصميم.
/// </summary>
[Route("api/v1/tailoring"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringSetupController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringSettingsScreenId;
    private const string Readers = PermissionCatalog.TailoringCustomersScreenId + "," + PermissionCatalog.TailoringOrdersScreenId;
    private readonly ITailoringSetupService _setup;
    public TailoringSetupController(ITailoringSetupService setup) => _setup = setup;

    [HttpGet("settings"), RequireScreen(Screen, AlsoAllow = Readers)]
    public async Task<IActionResult> Settings(CancellationToken ct) => Success(await _setup.SettingsAsync(ct));

    [HttpPut("settings"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveSettings([FromBody] SaveTailoringSettingsDto request, CancellationToken ct)
        => Success(await _setup.SaveSettingsAsync(request, ct));

    [HttpGet("garment-types"), RequireScreen(Screen, AlsoAllow = Readers)]
    public async Task<IActionResult> GarmentTypes([FromQuery] bool includeInactive, CancellationToken ct)
        => Success(await _setup.GarmentTypesAsync(includeInactive, ct));

    [HttpGet("garment-types/{id:guid}"), RequireScreen(Screen, AlsoAllow = Readers)]
    public async Task<IActionResult> GarmentType(Guid id, CancellationToken ct) => Success(await _setup.GarmentTypeAsync(id, ct));

    [HttpPost("garment-types"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateGarmentType([FromBody] SaveTailoringGarmentTypeDto request, CancellationToken ct)
        => Success(await _setup.CreateGarmentTypeAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("garment-types/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateGarmentType(Guid id, [FromBody] SaveTailoringGarmentTypeDto request, CancellationToken ct)
        => Success(await _setup.UpdateGarmentTypeAsync(id, request, ct));

    [HttpDelete("garment-types/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteGarmentType(Guid id, CancellationToken ct)
    {
        await _setup.DeleteGarmentTypeAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("garment-types/{id:guid}/complete-defaults"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CompleteDefaults(Guid id, CancellationToken ct) => Success(await _setup.CompleteDefaultsAsync(id, ct));

    [HttpGet("stages"), RequireScreen(Screen, AlsoAllow = Readers)]
    public async Task<IActionResult> Stages(CancellationToken ct) => Success(await _setup.StagesAsync(ct));

    [HttpPost("stages"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateStage([FromBody] SaveTailoringStageDto request, CancellationToken ct)
        => Success(await _setup.CreateStageAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("stages/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateStage(Guid id, [FromBody] SaveTailoringStageDto request, CancellationToken ct)
        => Success(await _setup.UpdateStageAsync(id, request, ct));

    [HttpDelete("stages/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteStage(Guid id, CancellationToken ct)
    {
        await _setup.DeleteStageAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>ملفات عملاء الخياطة وقياساتهم (شاشة العملاء؛ النسخة الجديدة = إنشاء، ولا تعديل ولا حذف للنسخ).</summary>
[Route("api/v1/tailoring/customers"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringCustomersController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringCustomersScreenId;
    private const string Orders = PermissionCatalog.TailoringOrdersScreenId;
    private readonly ITailoringCustomerService _customers;
    public TailoringCustomersController(ITailoringCustomerService customers) => _customers = customers;

    [HttpGet("load"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _customers.LoadAsync(loadOptions, ct));

    [HttpGet("{customerId:guid}"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Get(Guid customerId, CancellationToken ct) => Success(await _customers.GetAsync(customerId, ct));

    [HttpPut("{customerId:guid}/profile"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveProfile(Guid customerId, [FromBody] SaveTailoringCustomerProfileDto request, CancellationToken ct)
        => Success(await _customers.SaveProfileAsync(customerId, request, ct));

    [HttpPost("{customerId:guid}/measurement-profiles"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> CreateProfile(Guid customerId, [FromBody] SaveTailoringMeasurementProfileDto request, CancellationToken ct)
        => Success(await _customers.CreateMeasurementProfileAsync(customerId, request, ct), Messages.CreatedSuccessfully);

    [HttpPut("measurement-profiles/{profileId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateProfile(Guid profileId, [FromBody] SaveTailoringMeasurementProfileDto request, CancellationToken ct)
        => Success(await _customers.UpdateMeasurementProfileAsync(profileId, request, ct));

    [HttpDelete("measurement-profiles/{profileId:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> DeleteProfile(Guid profileId, CancellationToken ct)
    {
        await _customers.DeleteMeasurementProfileAsync(profileId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpGet("measurement-profiles/{profileId:guid}/versions"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Versions(Guid profileId, CancellationToken ct) => Success(await _customers.VersionsAsync(profileId, ct));

    [HttpPost("measurement-profiles/{profileId:guid}/versions"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> AddVersion(Guid profileId, [FromBody] CreateTailoringMeasurementVersionDto request, CancellationToken ct)
        => Success(await _customers.AddVersionAsync(profileId, request, ct), Messages.CreatedSuccessfully);

    [HttpGet("measurements/compare"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Compare([FromQuery] Guid from, [FromQuery] Guid to, CancellationToken ct)
        => Success(await _customers.CompareAsync(from, to, ct));
}

/// <summary>
/// أوامر التفصيل (شاشة الأوامر): الإنشاء والتعديل للمسودة، والتأكيد والدفعة المقدمة = اعتماد، والإرجاع للمسودة = إعادة فتح، والإلغاء.
/// صلاحية «خصم يدوي» تفرضها الخدمة على السعر المخالف والخصم.
/// </summary>
[Route("api/v1/tailoring/orders"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringOrdersController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringOrdersScreenId;
    private readonly ITailoringOrderService _orders;
    public TailoringOrdersController(ITailoringOrderService orders) => _orders = orders;

    [HttpGet("load"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct) => Ok(await _orders.LoadAsync(loadOptions, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _orders.GetAsync(id, ct));

    [HttpPost("preview"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Preview([FromBody] SaveTailoringOrderDto request, [FromQuery] Guid? id, CancellationToken ct)
        => Success(await _orders.PreviewAsync(request, id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveTailoringOrderDto request, CancellationToken ct)
        => Success(await _orders.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveTailoringOrderDto request, CancellationToken ct)
        => Success(await _orders.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _orders.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("{id:guid}/confirm"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct) => Success(await _orders.ConfirmAsync(id, ct));

    [HttpPost("{id:guid}/reopen"), RequireScreen(Screen, ScreenAction.Reopen)]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] TailoringReasonDto request, CancellationToken ct)
        => Success(await _orders.ReopenAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] TailoringReasonDto request, CancellationToken ct)
        => Success(await _orders.CancelAsync(id, request, ct));

    [HttpPost("{id:guid}/advance"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Advance(Guid id, [FromBody] TailoringAdvanceRequestDto request, CancellationToken ct)
        => Success(await _orders.AdvanceAsync(id, request, ct));

    [HttpPost("{id:guid}/advance/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> CancelAdvance(Guid id, [FromBody] TailoringReasonDto request, CancellationToken ct)
        => Success(await _orders.CancelAdvanceAsync(id, request, ct));
}

/// <summary>لوحة الإنتاج (شاشة الإنتاج): الإسناد ونقل المراحل = تعديل؛ الخروج من الجودة تفرض الخدمة اعتماد شاشة الجودة.</summary>
[Route("api/v1/tailoring/production"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringProductionController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringProductionScreenId;
    private readonly ITailoringProductionService _production;
    public TailoringProductionController(ITailoringProductionService production) => _production = production;

    [HttpGet("board")]
    public async Task<IActionResult> Board(CancellationToken ct) => Success(await _production.BoardAsync(ct));

    [HttpPost("assign"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Assign([FromBody] TailoringAssignDto request, CancellationToken ct)
    {
        await _production.AssignAsync(request, ct);
        return Success(Messages.UpdatedSuccessfully);
    }

    [HttpPost("garments/{id:guid}/move"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Move(Guid id, [FromBody] TailoringMoveDto request, CancellationToken ct)
    {
        await _production.MoveAsync(id, request, ct);
        return Success(Messages.UpdatedSuccessfully);
    }

    [HttpGet("garments/{id:guid}/history"), RequireScreen(Screen, AlsoAllow = PermissionCatalog.TailoringOrdersScreenId)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) => Success(await _production.HistoryAsync(id, ct));
}

/// <summary>البروفات وطلبات التعديل (شاشة البروفات).</summary>
[Route("api/v1/tailoring"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringFittingsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringFittingsScreenId;
    private readonly ITailoringProductionService _production;
    public TailoringFittingsController(ITailoringProductionService production) => _production = production;

    [HttpGet("fittings")]
    public async Task<IActionResult> Fittings([FromQuery] Guid? orderId, [FromQuery] bool upcomingOnly, CancellationToken ct)
        => Success(await _production.FittingsAsync(orderId, upcomingOnly, ct));

    [HttpPost("fittings")]
    public async Task<IActionResult> Schedule([FromBody] SaveTailoringFittingDto request, CancellationToken ct)
        => Success(await _production.ScheduleFittingAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("fittings/{id:guid}/result"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Result(Guid id, [FromBody] TailoringFittingResultDto request, CancellationToken ct)
        => Success(await _production.RecordFittingAsync(id, request, ct));

    [HttpGet("modifications")]
    public async Task<IActionResult> Modifications([FromQuery] Guid? orderId, [FromQuery] bool openOnly, CancellationToken ct)
        => Success(await _production.ModificationsAsync(orderId, openOnly, ct));

    [HttpPost("modifications")]
    public async Task<IActionResult> CreateModification([FromBody] SaveTailoringModificationDto request, CancellationToken ct)
        => Success(await _production.CreateModificationAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("modifications/{id:guid}/complete"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct) => Success(await _production.CompleteModificationAsync(id, ct));

    [HttpPost("modifications/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> CancelModification(Guid id, CancellationToken ct) => Success(await _production.CancelModificationAsync(id, ct));
}

/// <summary>صرف الأقمشة والمواد وإرجاعها (شاشة المواد)، والربحية الفعلية للأمر (تفرض الخدمة رؤية التكلفة).</summary>
[Route("api/v1/tailoring/materials"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringMaterialsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringMaterialsScreenId;
    private readonly ITailoringCostingService _costing;
    public TailoringMaterialsController(ITailoringCostingService costing) => _costing = costing;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? orderId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _costing.MaterialsAsync(orderId, from, to, ct));

    [HttpPost("issue"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Issue([FromBody] IssueTailoringMaterialDto request, CancellationToken ct)
        => Success(await _costing.IssueAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("return"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Return([FromBody] ReturnTailoringMaterialDto request, CancellationToken ct)
        => Success(await _costing.ReturnAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpGet("profitability/{orderId:guid}"), RequireScreen(PermissionCatalog.TailoringOrdersScreenId)]
    public async Task<IActionResult> Profitability(Guid orderId, CancellationToken ct) => Success(await _costing.ProfitabilityAsync(orderId, ct));
}

/// <summary>الخياطون: ملخص الأجور والقطع المفتوحة، وأجورهم الخاصة بالمراحل، وسجل أجور القطعة (اليدوي = إنشاء، الإلغاء = تعديل).</summary>
[Route("api/v1/tailoring/workers"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringWorkersController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringWorkersScreenId;
    private readonly ITailoringCostingService _costing;
    public TailoringWorkersController(ITailoringCostingService costing) => _costing = costing;

    [HttpGet]
    public async Task<IActionResult> Workers([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _costing.WorkersAsync(from, to, ct));

    [HttpPut("{employeeId:guid}/rates"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveRates(Guid employeeId, [FromBody] SaveTailoringWorkerRatesDto request, CancellationToken ct)
    {
        await _costing.SaveWorkerRatesAsync(employeeId, request, ct);
        return Success(Messages.UpdatedSuccessfully);
    }

    [HttpGet("labor"), RequireScreen(Screen, AlsoAllow = PermissionCatalog.TailoringOrdersScreenId)]
    public async Task<IActionResult> Labor([FromQuery] Guid? orderId, [FromQuery] Guid? employeeId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _costing.LaborAsync(orderId, employeeId, from, to, ct));

    [HttpPost("labor"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> AddLabor([FromBody] SaveTailoringLaborDto request, CancellationToken ct)
        => Success(await _costing.AddLaborAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("labor/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CancelLabor(Guid id, [FromBody] TailoringReasonDto request, CancellationToken ct)
        => Success(await _costing.CancelLaborAsync(id, request.Reason, ct));
}

/// <summary>
/// فحص الجودة (الخدمة تفرض اعتماد أو رفض <c>tailoring-quality</c>)، والتسليم وفاتورته وإلغاؤه (شاشة التسليم)، وإغلاق الأمر.
/// </summary>
[Route("api/v1/tailoring"), RequireScreen(Screen), RequireModule(PlatformModules.Tailoring)]
public class TailoringDeliveryController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.TailoringDeliveryScreenId;
    private readonly ITailoringDeliveryService _delivery;
    public TailoringDeliveryController(ITailoringDeliveryService delivery) => _delivery = delivery;

    [HttpPost("quality/inspect"), RequireScreen(PermissionCatalog.TailoringQualityScreenId, ScreenAction.View)]
    public async Task<IActionResult> Inspect([FromBody] TailoringInspectDto request, CancellationToken ct)
    {
        await _delivery.InspectAsync(request, ct);
        return Success(Messages.UpdatedSuccessfully);
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> List([FromQuery] Guid? orderId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _delivery.ListAsync(orderId, from, to, ct));

    [HttpGet("deliveries/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _delivery.GetAsync(id, ct));

    [HttpGet("orders/{orderId:guid}/delivery"), RequireScreen(Screen, AlsoAllow = PermissionCatalog.TailoringOrdersScreenId)]
    public async Task<IActionResult> Order(Guid orderId, CancellationToken ct) => Success(await _delivery.OrderAsync(orderId, ct));

    [HttpPost("deliveries/preview"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Preview([FromBody] TailoringDeliveryPreviewRequestDto request, CancellationToken ct)
        => Success(await _delivery.PreviewAsync(request, ct));

    [HttpPost("deliveries"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Deliver([FromBody] DeliverTailoringDto request, CancellationToken ct)
        => Success(await _delivery.DeliverAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("deliveries/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] TailoringReasonDto request, CancellationToken ct)
        => Success(await _delivery.CancelAsync(id, request, ct));

    [HttpPost("orders/{orderId:guid}/close"), RequireScreen(PermissionCatalog.TailoringOrdersScreenId, ScreenAction.Close)]
    public async Task<IActionResult> Close(Guid orderId, CancellationToken ct) => Success(await _delivery.CloseOrderAsync(orderId, ct));
}

/// <summary>لوحة الخياطة: لمن يرى الأوامر أو الإنتاج أو التسليم أو التقارير.</summary>
[Route("api/v1/tailoring/dashboard"), RequireModule(PlatformModules.Tailoring),
 RequireScreen(PermissionCatalog.TailoringOrdersScreenId, AlsoAllow = PermissionCatalog.TailoringProductionScreenId + "," + PermissionCatalog.TailoringDeliveryScreenId + "," + PermissionCatalog.TailoringReportsScreenId)]
public class TailoringDashboardController : ErpControllerBase
{
    private readonly ITailoringDashboardService _dashboard;
    public TailoringDashboardController(ITailoringDashboardService dashboard) => _dashboard = dashboard;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await _dashboard.DashboardAsync(ct));
}

/// <summary>تقارير الخياطة (شاشة التقارير؛ الخدمة تفرض رؤية التكلفة لتقريرَي الربحية والأجور).</summary>
[Route("api/v1/tailoring/reports"), RequireScreen(PermissionCatalog.TailoringReportsScreenId), RequireModule(PlatformModules.Tailoring)]
public class TailoringReportsController : ErpControllerBase
{
    private readonly ITailoringReportService _reports;
    public TailoringReportsController(ITailoringReportService reports) => _reports = reports;

    [HttpGet("{key}")]
    public async Task<IActionResult> Report(string key, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _reports.ReportAsync(key, from, to, ct));
}
