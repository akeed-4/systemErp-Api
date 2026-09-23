using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class MaterialRequisitionsController : BaseCrudController<MaterialRequisition>
{
    public MaterialRequisitionsController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public override async Task<IActionResult> GetAll()
    {
        return Ok(await _context.MaterialRequisitions.Include(m => m.Items).ToListAsync());
    }

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(Guid id)
    {
        var requisition = await _context.MaterialRequisitions.Include(m => m.Items).FirstOrDefaultAsync(m => m.Id == id);
        if (requisition == null) return NotFound();
        return Ok(requisition);
    }
}
