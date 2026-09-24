using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/loyalty"), RequireScreen("sales")]
public class PosLoyaltyController : ErpControllerBase
{
    private readonly IPosLoyaltyService _loyalty;
    public PosLoyaltyController(IPosLoyaltyService loyalty) => _loyalty = loyalty;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _loyalty.ListAsync(q, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _loyalty.GetAsync(id, ct));

    [HttpGet("customer/{customerId:guid}")]
    public async Task<IActionResult> ByCustomer(Guid customerId, CancellationToken ct) => Success(await _loyalty.GetByCustomerAsync(customerId, ct));
}
