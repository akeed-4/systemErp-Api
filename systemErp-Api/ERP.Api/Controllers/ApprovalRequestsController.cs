using ERP.Api.Controllers.Common;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class ApprovalRequestsController : BaseCrudController<ApprovalRequest>
{
    public ApprovalRequestsController(ApplicationDbContext context) : base(context)
    {
    }

    [HttpGet]
    public override async Task<IActionResult> GetAll()
    {
        return Ok(await _context.ApprovalRequests.Include(a => a.History).ToListAsync());
    }

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(Guid id)
    {
        var request = await _context.ApprovalRequests.Include(a => a.History).FirstOrDefaultAsync(a => a.Id == id);
        if (request == null) return NotFound();
        return Ok(request);
    }
}
