using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>المصروفات المدفوعة مقدماً (اشتراك تم السنوي) وإطفاؤها الشهري بزر «ترحيل إطفاء الشهر».</summary>
[Route("api/v1/prepaidexpenses"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class PrepaidExpensesController : ErpControllerBase
{
    private readonly IPrepaidExpenseService _service;
    public PrepaidExpensesController(IPrepaidExpenseService service) => _service = service;

    /// <summary>status اختياري: active | completed | cancelled.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _service.ListAsync(q, ct));

    [HttpPost]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> Create([FromBody] CreatePrepaidExpenseDto dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<PrepaidExpenseDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Success(await _service.CancelAsync(id, ct), Messages.UpdatedSuccessfully);

    /// <summary>ترحيل إطفاء الشهر (period بصيغة yyyy-MM، فارغ = الشهر الحالي). آمن للتكرار: الشهر لا يُطفأ مرتين.</summary>
    [HttpPost("amortize")]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> Amortize([FromBody] AmortizePrepaidExpensesDto? dto, CancellationToken ct)
        => Success(await _service.AmortizeAsync(dto ?? new AmortizePrepaidExpensesDto(), ct), Messages.PrepaidAmortized);
}
