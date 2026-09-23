using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class CommercialOrdersController : BaseCrudController<CommercialOrder>
{
    public CommercialOrdersController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public override async Task<IActionResult> GetAll()
    {
        return Ok(await _context.CommercialOrders.Include(c => c.Items).ToListAsync());
    }

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(Guid id)
    {
        var order = await _context.CommercialOrders.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == id);
        if (order == null) return NotFound();
        return Ok(order);
    }
}
