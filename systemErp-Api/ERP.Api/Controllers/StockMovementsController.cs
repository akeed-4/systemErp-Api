using ERP.Api.Controllers.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class StockMovementsController : BaseCrudController<StockMovement>
{
    private readonly IInventoryService _inventoryService;

    public StockMovementsController(ApplicationDbContext context, IInventoryService inventoryService) : base(context)
    {
        _inventoryService = inventoryService;
    }

    [HttpPost("record")]
    public async Task<IActionResult> Record([FromBody] ERP.Application.Interfaces.StockMovementDto dto)
    {
        var movement = await _inventoryService.RecordMovementAsync(dto);
        return Ok(movement);
    }
}
