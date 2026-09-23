using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class LookupController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public LookupController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("accounts")]
    public async Task<IActionResult> GetAccounts()
    {
        return Ok(await _context.Accounts
            .OrderBy(a => a.Code)
            .Select(a => new { a.Code, a.NameAr, a.NameEn, a.Type })
            .ToListAsync());
    }

    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers()
    {
        return Ok(await _context.Customers
            .Select(c => new { c.Id, c.NameAr, c.NameEn, c.VatNumber })
            .ToListAsync());
    }

    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers()
    {
        return Ok(await _context.Suppliers
            .Select(s => new { s.Id, s.NameAr, s.NameEn, s.VatNumber })
            .ToListAsync());
    }

    [HttpGet("products")]
    public async Task<IActionResult> GetProducts()
    {
        return Ok(await _context.Products
            .Select(p => new { p.Id, p.Sku, p.NameAr, p.SellingPrice, p.AverageCost, p.CurrentStock })
            .ToListAsync());
    }

    [HttpGet("warehouses")]
    public async Task<IActionResult> GetWarehouses()
    {
        return Ok(await _context.Warehouses
            .Select(w => new { w.Id, w.NameAr })
            .ToListAsync());
    }

    [HttpGet("cost-centers")]
    public async Task<IActionResult> GetCostCenters()
    {
        return Ok(await _context.CostCenters
            .Select(c => new { c.Code, c.NameAr })
            .ToListAsync());
    }

    [HttpGet("currencies")]
    public async Task<IActionResult> GetCurrencies()
    {
        return Ok(await _context.Currencies
            .Select(c => new { c.Code, c.NameAr, c.ExchangeRate })
            .ToListAsync());
    }
}
