using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>إعدادات معرض السيارات (افتراضي الرسوم البيعية، مدة الحجز، نسبة الضريبة). يحددها مدير المعرض ومحاسبه.</summary>
[Route("api/v1/carshowroomsettings"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class CarShowroomSettingsController : ErpControllerBase
{
    private readonly ICarShowroomSettingsService _service;
    public CarShowroomSettingsController(ICarShowroomSettingsService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await _service.GetAsync(ct));

    [HttpPut]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> Update([FromBody] CarShowroomSettingsDto dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(dto, ct), Messages.UpdatedSuccessfully);
}

/// <summary>حجز السيارات: حجز يدوي بتاريخ انتهاء، وعرض الحجوزات التي أنشأها عربون أو عقد.</summary>
[Route("api/v1/vehiclereservations"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class VehicleReservationsController : ErpControllerBase
{
    private readonly IVehicleReservationService _service;
    public VehicleReservationsController(IVehicleReservationService service) => _service = service;

    /// <summary>status اختياري: active | released | expired | converted.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, [FromQuery] Guid? vehicleId, CancellationToken ct)
        => Success(await _service.ListAsync(q, vehicleId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVehicleReservationDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<VehicleReservationDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPost("{id:guid}/extend")]
    [RequireScreen("car-showroom", ScreenAction.Edit)]
    public async Task<IActionResult> Extend(Guid id, [FromBody] ExtendVehicleReservationDto dto, CancellationToken ct)
        => Success(await _service.ExtendAsync(id, dto, ct), Messages.UpdatedSuccessfully);

    [HttpPost("{id:guid}/cancel")]
    [RequireScreen("car-showroom", ScreenAction.Edit)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] CancelVehicleReservationDto? dto, CancellationToken ct)
        => Success(await _service.CancelAsync(id, dto, ct), Messages.ReservationCancelled);
}

/// <summary>عمليات «تم» على السيارات، ونقطة تحديث حالتها (يستدعيها الربط مع تم أو المستخدم لحسم عملية مجهولة النتيجة).</summary>
[Route("api/v1/tammoperations"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class TammOperationsController : ErpControllerBase
{
    private readonly ITammOperationService _service;
    public TammOperationsController(ITammOperationService service) => _service = service;

    /// <summary>status اختياري: pending | success | failed | unknown.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, [FromQuery] Guid? vehicleId, CancellationToken ct)
        => Success(await _service.ListAsync(q, vehicleId, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTammOperationDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<TammOperationDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    /// <summary>النجاح ينشئ مصروف العملية مرة واحدة؛ الفشل والمجهول لا ينشئان شيئاً. تكرار النتيجة نفسها آمن.</summary>
    [HttpPost("{id:guid}/status")]
    [RequireScreen("car-showroom", ScreenAction.Edit)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTammOperationStatusDto dto, CancellationToken ct)
        => Success(await _service.UpdateStatusAsync(id, dto, ct), Messages.TammStatusUpdated);
}

/// <summary>محفظة «تم»: الرصيد الدفتري وحركاته، شحنها من البنك، ومطابقتها مع كشف تم.</summary>
[Route("api/v1/tammwallet"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class TammWalletController : ErpControllerBase
{
    private readonly ITammWalletService _service;
    public TammWalletController(ITammWalletService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _service.GetAsync(q, ct));

    [HttpPost("topup")]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> TopUp([FromBody] TammWalletTopUpDto dto, CancellationToken ct)
        => Success(await _service.TopUpAsync(dto, ct), Messages.TammWalletToppedUp);

    [HttpPost("reconcile")]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> Reconcile([FromBody] TammWalletReconcileDto dto, CancellationToken ct)
        => Success(await _service.ReconcileAsync(dto, ct), Messages.TammWalletReconciled);
}
