using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IAccountingService _accountingService;

    public DashboardController(ApplicationDbContext context, IAccountingService accountingService)
    {
        _context = context;
        _accountingService = accountingService;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);

        var salesThisMonth = await _context.Invoices
            .Where(i => i.IssueDate >= startOfMonth && (i.InvoiceType == ERP.Domain.Enums.InvoiceType.StandardTaxInvoice || i.InvoiceType == ERP.Domain.Enums.InvoiceType.SimplifiedTaxInvoice))
            .SumAsync(i => i.GrandTotal);

        var collectionThisMonth = await _context.Vouchers
            .Where(v => v.Date >= startOfMonth && v.Type == ERP.Domain.Enums.VoucherType.Receipt)
            .SumAsync(v => v.Amount);

        var lowStockCount = await _context.Products
            .Where(p => p.CurrentStock <= p.MinStockLevel)
            .CountAsync();

        var pendingApprovals = await _context.ApprovalRequests
            .Where(a => a.Status == "Pending")
            .CountAsync();

        var financialSummary = await _accountingService.GetFinancialSummaryAsync();

        return Ok(new
        {
            SalesThisMonth = salesThisMonth,
            CollectionThisMonth = collectionThisMonth,
            LowStockCount = lowStockCount,
            PendingApprovals = pendingApprovals,
            NetProfit = financialSummary.NetProfitOrLoss,
            TotalAssets = financialSummary.TotalAssets
        });
    }

    [HttpGet("recent-activity")]
    public async Task<IActionResult> GetRecentActivity()
    {
        var recentInvoices = await _context.Invoices
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new { i.InvoiceNumber, i.PartyName, i.GrandTotal, i.CreatedAt, Type = "Invoice" })
            .ToListAsync();

        var recentVouchers = await _context.Vouchers
            .OrderByDescending(v => v.CreatedAt)
            .Take(5)
            .Select(v => new { VoucherNumber = v.VoucherNumber, v.PartyName, Amount = v.Amount, v.CreatedAt, Type = "Voucher" })
            .ToListAsync();

        return Ok(new { Invoices = recentInvoices, Vouchers = recentVouchers });
    }
}
