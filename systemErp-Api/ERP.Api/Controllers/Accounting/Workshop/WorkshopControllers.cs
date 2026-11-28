using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// إعداد الورشة (شاشة إعدادات الورش؛ العرض للقراءة والتعديل لكل كتابة). القوائم المرجعية تكفيها أيضاً شاشة أوامر العمل لاختيارها هناك.
/// </summary>
[Route("api/v1/workshop"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkshopSetupController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopSettingsScreenId;
    private const string Orders = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IWorkshopSetupService _setup;
    public WorkshopSetupController(IWorkshopSetupService setup) => _setup = setup;

    [HttpGet("settings"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Settings(CancellationToken ct) => Success(await _setup.SettingsAsync(ct));

    [HttpPut("settings"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveSettings([FromBody] SaveWorkshopSettingsDto request, CancellationToken ct)
        => Success(await _setup.SaveSettingsAsync(request, ct));

    [HttpGet("stages"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Stages(CancellationToken ct) => Success(await _setup.StagesAsync(ct));

    [HttpPost("stages"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateStage([FromBody] SaveWorkshopStageDto request, CancellationToken ct)
        => Success(await _setup.CreateStageAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("stages/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateStage(Guid id, [FromBody] SaveWorkshopStageDto request, CancellationToken ct)
        => Success(await _setup.UpdateStageAsync(id, request, ct));

    [HttpDelete("stages/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteStage(Guid id, CancellationToken ct)
    {
        await _setup.DeleteStageAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpGet("pause-reasons"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> PauseReasons(CancellationToken ct) => Success(await _setup.PauseReasonsAsync(ct));

    [HttpPost("pause-reasons"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreatePauseReason([FromBody] SaveWorkshopPauseReasonDto request, CancellationToken ct)
        => Success(await _setup.CreatePauseReasonAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("pause-reasons/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdatePauseReason(Guid id, [FromBody] SaveWorkshopPauseReasonDto request, CancellationToken ct)
        => Success(await _setup.UpdatePauseReasonAsync(id, request, ct));

    [HttpDelete("pause-reasons/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeletePauseReason(Guid id, CancellationToken ct)
    {
        await _setup.DeletePauseReasonAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpGet("services"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Services([FromQuery] bool activeOnly, CancellationToken ct) => Success(await _setup.ServicesAsync(activeOnly, ct));

    [HttpPost("services"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateService([FromBody] SaveWorkshopServiceDto request, CancellationToken ct)
        => Success(await _setup.CreateServiceAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("services/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateService(Guid id, [FromBody] SaveWorkshopServiceDto request, CancellationToken ct)
        => Success(await _setup.UpdateServiceAsync(id, request, ct));

    [HttpDelete("services/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteService(Guid id, CancellationToken ct)
    {
        await _setup.DeleteServiceAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpGet("inspection-templates"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Templates([FromQuery] string? usage, CancellationToken ct) => Success(await _setup.TemplatesAsync(usage, ct));

    [HttpGet("inspection-templates/{id:guid}"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Template(Guid id, CancellationToken ct) => Success(await _setup.GetTemplateAsync(id, ct));

    [HttpPost("inspection-templates"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveInspectionTemplateDto request, CancellationToken ct)
        => Success(await _setup.CreateTemplateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("inspection-templates/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] SaveInspectionTemplateDto request, CancellationToken ct)
        => Success(await _setup.UpdateTemplateAsync(id, request, ct));

    [HttpDelete("inspection-templates/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
    {
        await _setup.DeleteTemplateAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>سجل مركبات العملاء (شاشة المركبات). القراءة تكفيها شاشة أوامر العمل لاختيار المركبة في الاستقبال.</summary>
[Route("api/v1/workshop/vehicles"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class ServiceVehiclesController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopVehiclesScreenId;
    private const string Orders = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IServiceVehicleService _vehicles;
    public ServiceVehiclesController(IServiceVehicleService vehicles) => _vehicles = vehicles;

    [HttpGet, RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> List([FromQuery] Guid? customerId, [FromQuery] string? search, CancellationToken ct)
        => Success(await _vehicles.ListAsync(customerId, search, ct));

    [HttpGet("{id:guid}"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _vehicles.GetAsync(id, ct));

    [HttpGet("{id:guid}/history"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) => Success(await _vehicles.HistoryAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveServiceVehicleDto request, CancellationToken ct)
        => Success(await _vehicles.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveServiceVehicleDto request, CancellationToken ct)
        => Success(await _vehicles.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _vehicles.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>
/// الاستقبال وأوامر العمل (شاشة أوامر العمل): «إنشاء» = استقبال أو مركبة إضافية، «تعديل» = التفاصيل والأضرار والانتقالات (و«معتمد» يتطلب
/// الاعتماد في الخدمة)، «إلغاء» بسبب. الصور عبر المرفقات المشتركة (`api/v1/attachments/work_order/{id}` و`work_order_damage/{damageId}`).
/// </summary>
[Route("api/v1/workshop"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkOrdersController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IWorkOrderService _orders;
    public WorkOrdersController(IWorkOrderService orders) => _orders = orders;

    [HttpGet("receptions")]
    public async Task<IActionResult> Receptions([FromQuery] Guid? customerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _orders.ReceptionsAsync(customerId, from, to, ct));

    [HttpGet("receptions/{id:guid}")]
    public async Task<IActionResult> Reception(Guid id, CancellationToken ct) => Success(await _orders.GetReceptionAsync(id, ct));

    [HttpPost("receptions")]
    public async Task<IActionResult> CreateReception([FromBody] SaveWorkshopReceptionDto request, CancellationToken ct)
        => Success(await _orders.CreateReceptionAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("receptions/{id:guid}")]
    public async Task<IActionResult> UpdateReception(Guid id, [FromBody] UpdateWorkshopReceptionDto request, CancellationToken ct)
        => Success(await _orders.UpdateReceptionAsync(id, request, ct));

    [HttpPost("receptions/{id:guid}/vehicles")]
    public async Task<IActionResult> AddVehicle(Guid id, [FromBody] ReceptionVehicleDto request, CancellationToken ct)
        => Success(await _orders.AddVehicleAsync(id, request, ct), Messages.CreatedSuccessfully);

    /// <summary>`status=open` للأوامر القائمة، أو حالة محددة.</summary>
    [HttpGet("work-orders")]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] Guid? customerId, [FromQuery] Guid? vehicleId, [FromQuery] Guid? receptionId,
        CancellationToken ct) => Success(await _orders.ListAsync(status, customerId, vehicleId, receptionId, ct));

    [HttpGet("work-orders/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _orders.GetAsync(id, ct));

    [HttpPut("work-orders/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkOrderDto request, CancellationToken ct)
        => Success(await _orders.UpdateAsync(id, request, ct));

    [HttpPost("work-orders/{id:guid}/status"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeWorkOrderStatusDto request, CancellationToken ct)
        => Success(await _orders.ChangeStatusAsync(id, request, ct));

    [HttpPost("work-orders/{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] WorkshopReasonDto request, CancellationToken ct)
        => Success(await _orders.CancelAsync(id, request, ct));

    [HttpDelete("work-orders/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _orders.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("work-orders/{id:guid}/damages"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> AddDamage(Guid id, [FromBody] SaveDamageMarkDto request, CancellationToken ct)
        => Success(await _orders.AddDamageAsync(id, request, ct));

    [HttpPut("work-orders/{id:guid}/damages/{damageId:guid}")]
    public async Task<IActionResult> UpdateDamage(Guid id, Guid damageId, [FromBody] SaveDamageMarkDto request, CancellationToken ct)
        => Success(await _orders.UpdateDamageAsync(id, damageId, request, ct));

    [HttpDelete("work-orders/{id:guid}/damages/{damageId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteDamage(Guid id, Guid damageId, CancellationToken ct)
        => Success(await _orders.DeleteDamageAsync(id, damageId, ct));
}

/// <summary>
/// العمل على أمر العمل (شاشة أوامر العمل): «تعديل» = الفحوص والأسطر والتقديرات وحالة التنفيذ، و«اعتماد» = تسجيل رد العميل (تفرضه الخدمة)،
/// والاستثناء بالشاشة المقيدة `workshop-unapproved-execution`. صور نتائج الفحص عبر `api/v1/attachments/work_order_inspection_result/{resultId}`.
/// </summary>
[Route("api/v1/workshop/work-orders/{id:guid}"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkOrderWorkController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IWorkOrderWorkService _work;
    public WorkOrderWorkController(IWorkOrderWorkService work) => _work = work;

    [HttpGet("work")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _work.GetAsync(id, ct));

    [HttpPost("inspections"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateInspection(Guid id, [FromBody] CreateInspectionDto request, CancellationToken ct)
        => Success(await _work.CreateInspectionAsync(id, request, ct), Messages.CreatedSuccessfully);

    [HttpPut("inspections/{inspectionId:guid}")]
    public async Task<IActionResult> SaveInspection(Guid id, Guid inspectionId, [FromBody] SaveInspectionResultsDto request, CancellationToken ct)
        => Success(await _work.SaveInspectionAsync(id, inspectionId, request, ct));

    [HttpPost("inspections/{inspectionId:guid}/complete"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CompleteInspection(Guid id, Guid inspectionId, CancellationToken ct)
        => Success(await _work.CompleteInspectionAsync(id, inspectionId, ct));

    [HttpDelete("inspections/{inspectionId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteInspection(Guid id, Guid inspectionId, CancellationToken ct)
    {
        await _work.DeleteInspectionAsync(id, inspectionId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("lines"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> AddLine(Guid id, [FromBody] SaveWorkOrderLineDto request, CancellationToken ct)
        => Success(await _work.AddLineAsync(id, request, ct));

    [HttpPut("lines/{lineId:guid}")]
    public async Task<IActionResult> UpdateLine(Guid id, Guid lineId, [FromBody] SaveWorkOrderLineDto request, CancellationToken ct)
        => Success(await _work.UpdateLineAsync(id, lineId, request, ct));

    [HttpDelete("lines/{lineId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteLine(Guid id, Guid lineId, CancellationToken ct) => Success(await _work.DeleteLineAsync(id, lineId, ct));

    [HttpPost("lines/{lineId:guid}/execution"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> ChangeExecution(Guid id, Guid lineId, [FromBody] ChangeLineExecutionDto request, CancellationToken ct)
        => Success(await _work.ChangeExecutionAsync(id, lineId, request, ct));

    [HttpPost("lines/{lineId:guid}/exception"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> AddException(Guid id, Guid lineId, [FromBody] CreateExecutionExceptionDto request, CancellationToken ct)
        => Success(await _work.AddExceptionAsync(id, lineId, request, ct));

    [HttpPost("estimates"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CreateEstimate(Guid id, [FromBody] CreateEstimateDto request, CancellationToken ct)
        => Success(await _work.CreateEstimateAsync(id, request, ct), Messages.CreatedSuccessfully);

    [HttpPost("estimates/{estimateId:guid}/send"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SendEstimate(Guid id, Guid estimateId, CancellationToken ct) => Success(await _work.SendEstimateAsync(id, estimateId, ct));

    [HttpPost("estimates/{estimateId:guid}/respond"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> RespondEstimate(Guid id, Guid estimateId, [FromBody] RespondEstimateDto request, CancellationToken ct)
        => Success(await _work.RespondEstimateAsync(id, estimateId, request, ct));

    [HttpDelete("estimates/{estimateId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteEstimate(Guid id, Guid estimateId, CancellationToken ct)
    {
        await _work.DeleteEstimateAsync(id, estimateId, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>
/// تنفيذ أمر العمل: الساعات (شاشة ساعات الفنيين)، والقطع (شاشة القطع: «إنشاء» = حجز، «حذف» = تحرير، «ترحيل» = صرف، «إلغاء» = إرجاع)،
/// والخدمات الخارجية (تعديل أمر العمل). العرض بشاشة أوامر العمل.
/// </summary>
[Route("api/v1/workshop/work-orders/{id:guid}"), RequireScreen(Orders), RequireModule(PlatformModules.Workshop)]
public class WorkOrderExecutionController : ErpControllerBase
{
    private const string Orders = PermissionCatalog.WorkshopOrdersScreenId;
    private const string Times = PermissionCatalog.WorkshopTimeLogsScreenId;
    private const string Parts = PermissionCatalog.WorkshopPartsScreenId;
    private readonly IWorkOrderExecutionService _exec;
    public WorkOrderExecutionController(IWorkOrderExecutionService exec) => _exec = exec;

    [HttpGet("execution")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _exec.GetAsync(id, ct));

    [HttpPost("time-logs"), RequireScreen(Times, ScreenAction.Create)]
    public async Task<IActionResult> StartLog(Guid id, [FromBody] SaveTimeLogDto request, CancellationToken ct)
        => Success(await _exec.StartLogAsync(id, request, ct), Messages.CreatedSuccessfully);

    [HttpPost("time-logs/{logId:guid}/stop"), RequireScreen(Times, ScreenAction.Edit)]
    public async Task<IActionResult> StopLog(Guid id, Guid logId, [FromBody] StopTimeLogDto request, CancellationToken ct)
        => Success(await _exec.StopLogAsync(id, logId, request, ct));

    [HttpDelete("time-logs/{logId:guid}"), RequireScreen(Times, ScreenAction.Delete)]
    public async Task<IActionResult> DeleteLog(Guid id, Guid logId, CancellationToken ct)
    {
        await _exec.DeleteLogAsync(id, logId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("parts/reserve"), RequireScreen(Parts, ScreenAction.Create)]
    public async Task<IActionResult> Reserve(Guid id, [FromBody] ReservePartDto request, CancellationToken ct) => Success(await _exec.ReserveAsync(id, request, ct));

    [HttpDelete("parts/reservations/{reservationId:guid}"), RequireScreen(Parts, ScreenAction.Delete)]
    public async Task<IActionResult> Release(Guid id, Guid reservationId, CancellationToken ct) => Success(await _exec.ReleaseAsync(id, reservationId, ct));

    [HttpPost("parts/issue"), RequireScreen(Parts, ScreenAction.Post)]
    public async Task<IActionResult> Issue(Guid id, [FromBody] IssuePartDto request, CancellationToken ct) => Success(await _exec.IssueAsync(id, request, ct));

    [HttpPost("parts/return"), RequireScreen(Parts, ScreenAction.Cancel)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnPartDto request, CancellationToken ct) => Success(await _exec.ReturnAsync(id, request, ct));

    [HttpPost("external-services"), RequireScreen(Orders, ScreenAction.Edit)]
    public async Task<IActionResult> AddExternal(Guid id, [FromBody] SaveExternalServiceDto request, CancellationToken ct)
        => Success(await _exec.AddExternalAsync(id, request, ct));

    [HttpPost("external-services/{serviceId:guid}/complete"), RequireScreen(Orders, ScreenAction.Edit)]
    public async Task<IActionResult> CompleteExternal(Guid id, Guid serviceId, [FromBody] CompleteExternalServiceDto request, CancellationToken ct)
        => Success(await _exec.CompleteExternalAsync(id, serviceId, request, ct));

    [HttpPost("external-services/{serviceId:guid}/cancel"), RequireScreen(Orders, ScreenAction.Edit)]
    public async Task<IActionResult> CancelExternal(Guid id, Guid serviceId, [FromBody] WorkshopReasonDto request, CancellationToken ct)
        => Success(await _exec.CancelExternalAsync(id, serviceId, request, ct));
}

/// <summary>لوحة الفنيين: السجلات المفتوحة الآن (شاشة ساعات الفنيين).</summary>
[Route("api/v1/workshop/time-logs"), RequireScreen(PermissionCatalog.WorkshopTimeLogsScreenId), RequireModule(PlatformModules.Workshop)]
public class WorkshopTimeLogsController : ErpControllerBase
{
    private readonly IWorkOrderExecutionService _exec;
    public WorkshopTimeLogsController(IWorkOrderExecutionService exec) => _exec = exec;

    [HttpGet("open")]
    public async Task<IActionResult> Open([FromQuery] Guid? employeeId, CancellationToken ct) => Success(await _exec.OpenLogsAsync(employeeId, ct));
}

/// <summary>
/// إنهاء أمر العمل (شاشة أوامر العمل): «اعتماد» = إصدار الفاتورة، «إعادة فتح» = إلغاء الفاتورة بسبب، «تعديل» = التسليم، «إغلاق» = الإغلاق؛
/// والضمانات: «إنشاء» = مطالبة (أمر ضمان)، «إلغاء» = إبطال الضمان بسبب.
/// </summary>
[Route("api/v1/workshop"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkOrderBillingController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IWorkOrderBillingService _billing;
    public WorkOrderBillingController(IWorkOrderBillingService billing) => _billing = billing;

    [HttpGet("work-orders/{id:guid}/billing-preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct) => Success(await _billing.PreviewAsync(id, ct));

    [HttpPost("work-orders/{id:guid}/invoice"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Invoice(Guid id, [FromBody] InvoiceWorkOrderDto request, CancellationToken ct) => Success(await _billing.InvoiceAsync(id, request, ct));

    [HttpPost("work-orders/{id:guid}/cancel-invoice"), RequireScreen(Screen, ScreenAction.Reopen)]
    public async Task<IActionResult> CancelInvoice(Guid id, [FromBody] WorkshopReasonDto request, CancellationToken ct)
        => Success(await _billing.CancelInvoiceAsync(id, request, ct));

    [HttpPost("work-orders/{id:guid}/deliver"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Deliver(Guid id, [FromBody] DeliverWorkOrderDto request, CancellationToken ct) => Success(await _billing.DeliverAsync(id, request, ct));

    [HttpPost("work-orders/{id:guid}/close"), RequireScreen(Screen, ScreenAction.Close)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Success(await _billing.CloseAsync(id, ct));

    [HttpGet("warranties"), RequireScreen(Screen, AlsoAllow = PermissionCatalog.WorkshopVehiclesScreenId)]
    public async Task<IActionResult> Warranties([FromQuery] Guid? vehicleId, [FromQuery] Guid? workOrderId, CancellationToken ct)
        => Success(await _billing.WarrantiesAsync(vehicleId, workOrderId, ct));

    [HttpPost("warranties/{warrantyId:guid}/claim"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Claim(Guid warrantyId, [FromBody] ClaimWarrantyDto request, CancellationToken ct)
        => Success(await _billing.ClaimWarrantyAsync(warrantyId, request, ct), Messages.CreatedSuccessfully);

    [HttpPost("warranties/{warrantyId:guid}/void"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Void(Guid warrantyId, [FromBody] WorkshopReasonDto request, CancellationToken ct)
        => Success(await _billing.VoidWarrantyAsync(warrantyId, request, ct));
}

/// <summary>
/// تقارير الورشة: اللوحة بشاشة أوامر العمل، والربحية والفنيون والقطع بشاشة رؤية التكلفة المقيدة (`workshop-cost-view`).
/// </summary>
[Route("api/v1/workshop/reports"), RequireScreen(Cost), RequireModule(PlatformModules.Workshop)]
public class WorkshopReportsController : ErpControllerBase
{
    private const string Cost = PermissionCatalog.WorkshopCostViewScreenId;
    private readonly IWorkshopReportService _reports;
    public WorkshopReportsController(IWorkshopReportService reports) => _reports = reports;

    [HttpGet("dashboard"), RequireScreen(PermissionCatalog.WorkshopOrdersScreenId)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Success(await _reports.DashboardAsync(ct));

    [HttpGet("profitability")]
    public async Task<IActionResult> Profitability([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Success(await _reports.ProfitabilityAsync(from, to, ct));

    [HttpGet("technicians")]
    public async Task<IActionResult> Technicians([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Success(await _reports.TechniciansAsync(from, to, ct));

    [HttpGet("parts")]
    public async Task<IActionResult> Parts([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Success(await _reports.PartsAsync(from, to, ct));

    /// <summary>فريق الفنيين بلا مبالغ: بشاشة أوامر العمل مثل اللوحة.</summary>
    [HttpGet("team"), RequireScreen(PermissionCatalog.WorkshopOrdersScreenId)]
    public async Task<IActionResult> Team([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Success(await _reports.TeamAsync(from, to, ct));
}
