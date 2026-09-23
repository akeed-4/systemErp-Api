using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class QuotationsController : BaseCrudController<Quotation>
{
    public QuotationsController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public override async Task<IActionResult> GetAll()
    {
        return Ok(await _context.Quotations.Include(q => q.Items).ToListAsync());
    }

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(Guid id)
    {
        var quotation = await _context.Quotations.Include(q => q.Items).FirstOrDefaultAsync(q => q.Id == id);
        if (quotation == null) return NotFound();
        return Ok(quotation);
    }
}
