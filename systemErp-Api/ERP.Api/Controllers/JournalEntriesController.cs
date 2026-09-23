using ERP.Api.Controllers.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class JournalEntriesController : BaseCrudController<JournalEntry>
{
    private readonly IAccountingService _accountingService;

    public JournalEntriesController(ApplicationDbContext context, IAccountingService accountingService) : base(context)
    {
        _accountingService = accountingService;
    }

    [HttpGet]
    public override async Task<IActionResult> GetAll()
    {
        return Ok(await _context.JournalEntries.Include(j => j.Lines).ToListAsync());
    }

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(Guid id)
    {
        var entry = await _context.JournalEntries.Include(j => j.Lines).FirstOrDefaultAsync(j => j.Id == id);
        if (entry == null) return NotFound();
        return Ok(entry);
    }

    [HttpPost("{id}/reverse")]
    public async Task<IActionResult> ReverseEntry(Guid id)
    {
        var reversal = await _accountingService.ReverseJournalEntryAsync(id);
        return Ok(reversal);
    }
}
