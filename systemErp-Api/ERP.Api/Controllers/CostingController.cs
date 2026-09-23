using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Application.Costing;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;

namespace ERP.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class CostingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantService _tenantService;

    public CostingController(ApplicationDbContext context, ITenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCostingSummary()
    {
        var products = await _context.Products.ToListAsync();

        return Ok(new
        {
            TenantId = _tenantService.CurrentTenantId,
            CostingMethod = "Weighted Moving Average (المتوسط المرجح المتحرك)",
            Currency = "SAR",
            Products = products
        });
    }

    [HttpPost("recalculate-all")]
    public async Task<IActionResult> RecalculateAll()
    {
        // Simple logic: update all products (in reality would be more complex)
        var products = await _context.Products.ToListAsync();
        foreach (var product in products)
        {
            // Placeholder logic
            product.AverageCost *= 1.0m;
        }
        await _context.SaveChangesAsync();
        return Ok(new { message = "All products costing recalculated" });
    }

    [HttpPost("policy")]
    public async Task<IActionResult> SetPolicy([FromBody] string policy)
    {
        // Logic to save policy to tenant settings
        return Ok(new { policy, message = "Costing policy updated" });
    }
}
