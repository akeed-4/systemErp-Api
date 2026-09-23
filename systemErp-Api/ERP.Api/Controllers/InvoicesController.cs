using ERP.Api.Controllers.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class InvoicesController : BaseCrudController<Invoice>
{
    private readonly IAccountingService _accountingService;
    private readonly IAccountSuggestionService _suggestionService;

    public InvoicesController(ApplicationDbContext context, IAccountingService accountingService, IAccountSuggestionService suggestionService) : base(context)
    {
        _accountingService = accountingService;
        _suggestionService = suggestionService;
    }

    [HttpGet("{id}/suggestions")]
    public async Task<IActionResult> GetSuggestions(Guid id)
    {
        var invoice = await _context.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null) return NotFound();
        return Ok(_suggestionService.GetInvoiceSuggestions(invoice));
    }

    [HttpGet]
    public override async Task<IActionResult> GetAll()
    {
        return Ok(await _context.Invoices.Include(i => i.Items).ToListAsync());
    }

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(Guid id)
    {
        var invoice = await _context.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null) return NotFound();
        return Ok(invoice);
    }

    [HttpPost("{id}/post")]
    public async Task<IActionResult> PostInvoice(Guid id)
    {
        var invoice = await _context.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null) return NotFound();
        if (invoice.Status == "Posted") return BadRequest("Invoice already posted");

        await _accountingService.PostAutomaticInvoiceJournalAsync(invoice);
        return Ok(new { message = "Invoice posted and journal entry generated", journalEntryId = invoice.JournalEntryId });
    }
}
