using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/costing"), RequireScreen("purchases")]
public class CostingController : ErpControllerBase
{
    private readonly ICostingService _costing;
    public CostingController(ICostingService costing) => _costing = costing;

    [HttpGet("policy")]
    public async Task<IActionResult> Get(CancellationToken ct) => Success(await _costing.GetPolicyAsync(ct));

    [HttpPut("policy")]
    public async Task<IActionResult> Set([FromBody] CreateCostingPolicyDto policy, CancellationToken ct)
        => Success(await _costing.SetPolicyAsync(policy, ct), "تم حفظ سياسة التكلفة");

    [HttpPost("recalculate-all")]
    public async Task<IActionResult> Recalculate(CancellationToken ct) => Success(await _costing.RecalculateAllAsync(ct));
}
