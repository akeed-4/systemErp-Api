using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;

namespace ERP.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public TenantsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetTenants()
    {
        var tenants = await _context.Tenants
            .ToListAsync();

        return Ok(tenants);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTenant(Guid id)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();
        return Ok(tenant);
    }

    [HttpPost]
    public async Task<IActionResult> CreateTenant(Tenant tenant)
    {
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetTenant), new { id = tenant.Id }, tenant);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTenant(Guid id, Tenant tenant)
    {
        if (id != tenant.Id) return BadRequest();
        _context.Entry(tenant).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTenant(Guid id)
    {
        var tenant = await _context.Tenants.FindAsync(id);
        if (tenant == null) return NotFound();
        _context.Tenants.Remove(tenant);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
