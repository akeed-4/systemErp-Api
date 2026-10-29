using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

/// <summary>دفعات جهاز الدفع المربوط بنقطة البيع (برنامج الربط على جهاز الكاشير ينقل نتيجة الجهاز).</summary>
[Route("api/v1/pos/TerminalPayments"), RequireScreen("sales")]
[RequireModule(PlatformModules.Pos)]
public class PosTerminalPaymentsController : ErpControllerBase
{
    private readonly ICardTerminalPaymentService _payments;
    public PosTerminalPaymentsController(ICardTerminalPaymentService payments) => _payments = payments;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? shiftId, [FromQuery] bool unresolved, CancellationToken ct)
        => Success(await _payments.ListAsync(shiftId, unresolved, ct));

    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _payments.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Start([FromBody] StartCardTerminalPaymentDto dto, CancellationToken ct) => Success(await _payments.StartAsync(dto, ct));

    [HttpPost("{id:guid}/result")]
    public async Task<IActionResult> Report(Guid id, [FromBody] ReportCardTerminalResultDto dto, CancellationToken ct) => Success(await _payments.ReportAsync(id, dto, ct));

    [HttpPost("{id:guid}/resolve"), RequireScreen("sales", ScreenAction.Approve)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveCardTerminalPaymentDto dto, CancellationToken ct) => Success(await _payments.ResolveAsync(id, dto, ct));
}
