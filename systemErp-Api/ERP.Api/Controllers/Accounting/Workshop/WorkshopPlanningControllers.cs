using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>التخطيط والجدولة (شاشة التخطيط): المنصات والجدول وإسناد أمر العمل. قراءة المنصات تكفيها شاشة أوامر العمل.</summary>
[Route("api/v1/workshop"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkshopPlanningController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopPlanningScreenId;
    private const string Orders = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IWorkshopPlanningService _planning;
    public WorkshopPlanningController(IWorkshopPlanningService planning) => _planning = planning;

    [HttpGet("bays"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Bays(CancellationToken ct) => Success(await _planning.BaysAsync(ct));

    [HttpPost("bays"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> CreateBay([FromBody] SaveWorkshopBayDto request, CancellationToken ct)
        => Success(await _planning.CreateBayAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("bays/{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateBay(Guid id, [FromBody] SaveWorkshopBayDto request, CancellationToken ct)
        => Success(await _planning.UpdateBayAsync(id, request, ct));

    [HttpDelete("bays/{id:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> DeleteBay(Guid id, CancellationToken ct)
    {
        await _planning.DeleteBayAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Success(await _planning.ScheduleAsync(from, to, warehouseId, ct));

    [HttpPut("work-orders/{id:guid}/plan"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Plan(Guid id, [FromBody] PlanWorkOrderDto request, CancellationToken ct)
        => Success(await _planning.PlanAsync(id, request, ct));
}

/// <summary>المواعيد (شاشة التخطيط): «إلغاء» = إلغاء الموعد بسبب، والوصول والتأكيد وعدم الحضور «تعديل».</summary>
[Route("api/v1/workshop/appointments"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkshopAppointmentsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopPlanningScreenId;
    private const string Orders = PermissionCatalog.WorkshopOrdersScreenId;
    private readonly IWorkshopAppointmentService _appointments;
    public WorkshopAppointmentsController(IWorkshopAppointmentService appointments) => _appointments = appointments;

    [HttpGet, RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? status, [FromQuery] Guid? customerId, CancellationToken ct)
        => Success(await _appointments.ListAsync(from, to, status, customerId, ct));

    [HttpGet("{id:guid}"), RequireScreen(Screen, AlsoAllow = Orders)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _appointments.GetAsync(id, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveWorkshopAppointmentDto request, CancellationToken ct)
        => Success(await _appointments.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveWorkshopAppointmentDto request, CancellationToken ct)
        => Success(await _appointments.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/status"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] AppointmentStatusDto request, CancellationToken ct)
        => Success(await _appointments.SetStatusAsync(id, request, ct));

    // الوصول بعد استقبال المركبة، فتكفيه شاشة أوامر العمل كذلك
    [HttpPost("{id:guid}/arrive"), RequireScreen(Screen, ScreenAction.Edit, AlsoAllow = Orders)]
    public async Task<IActionResult> Arrive(Guid id, [FromBody] AppointmentArriveDto request, CancellationToken ct)
        => Success(await _appointments.ArriveAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] WorkshopReasonDto request, CancellationToken ct)
        => Success(await _appointments.CancelAsync(id, request, ct));

    [HttpDelete("{id:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _appointments.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>المتابعة بعد التسليم وتذكيرات الصيانة: «إغلاق» = إنجاز، «إلغاء» = إلغاء بسبب.</summary>
[Route("api/v1/workshop/follow-ups"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkshopFollowUpsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopFollowUpsScreenId;
    private readonly IWorkshopFollowUpService _followUps;
    public WorkshopFollowUpsController(IWorkshopFollowUpService followUps) => _followUps = followUps;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] bool due, [FromQuery] Guid? vehicleId, CancellationToken ct)
        => Success(await _followUps.ListAsync(status, due, vehicleId, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveWorkshopFollowUpDto request, CancellationToken ct)
        => Success(await _followUps.CreateAsync(request, ct), Messages.CreatedSuccessfully);

    [HttpPost("{id:guid}/complete"), RequireScreen(Screen, ScreenAction.Close)]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteFollowUpDto request, CancellationToken ct)
        => Success(await _followUps.CompleteAsync(id, request, ct));

    [HttpPost("{id:guid}/cancel"), RequireScreen(Screen, ScreenAction.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] WorkshopReasonDto request, CancellationToken ct)
        => Success(await _followUps.CancelAsync(id, request, ct));
}

/// <summary>مهامي (الفني): الهوية من الموظف المربوط بالمستخدم، والإجراءات على سجله وأوامره فقط.</summary>
[Route("api/v1/workshop/my-jobs"), RequireScreen(Screen), RequireModule(PlatformModules.Workshop)]
public class WorkshopMyJobsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.WorkshopMyJobsScreenId;
    private readonly IWorkshopTechnicianService _tech;
    public WorkshopMyJobsController(IWorkshopTechnicianService tech) => _tech = tech;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await _tech.MyJobsAsync(ct));

    [HttpPost("{workOrderId:guid}/start"), RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Start(Guid workOrderId, [FromBody] MyJobStartDto request, CancellationToken ct)
        => Success(await _tech.StartAsync(workOrderId, request, ct));

    [HttpPost("stop"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Stop([FromBody] StopTimeLogDto request, CancellationToken ct) => Success(await _tech.StopAsync(request, ct));

    [HttpPost("{workOrderId:guid}/lines/{lineId:guid}/done"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> CompleteLine(Guid workOrderId, Guid lineId, CancellationToken ct)
        => Success(await _tech.CompleteLineAsync(workOrderId, lineId, ct));
}
