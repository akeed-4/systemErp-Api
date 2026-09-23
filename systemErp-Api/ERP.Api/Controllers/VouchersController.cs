using ERP.Api.Controllers.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class VouchersController : BaseCrudController<Voucher>
{
    private readonly IAccountingService _accountingService;

    public VouchersController(ApplicationDbContext context, IAccountingService accountingService) : base(context)
    {
        _accountingService = accountingService;
    }

    [HttpPost("{id}/post")]
    public async Task<IActionResult> PostVoucher(Guid id)
    {
        var voucher = await _context.Vouchers.FindAsync(id);
        if (voucher == null) return NotFound();
        if (voucher.JournalEntryId != null) return BadRequest("Voucher already posted");

        await _accountingService.PostAutomaticVoucherJournalAsync(voucher);
        return Ok(new { message = "Voucher posted and journal entry generated", journalEntryId = voucher.JournalEntryId });
    }
}
