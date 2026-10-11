using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>تحويلات المخزون بين المستودعات.</summary>
[Route("api/v1/stocktransfers"), RequireScreen("purchases")]
public class StockTransfersController : ErpControllerBase
{
    private readonly IStockTransferService _transfers;
    public StockTransfersController(IStockTransferService transfers) => _transfers = transfers;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PaginationParams query, CancellationToken ct) => Success(await _transfers.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _transfers.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStockTransferDto request, CancellationToken ct)
        => Success(await _transfers.CreateAsync(request, ct), Messages.StockTransferRecorded);
}
