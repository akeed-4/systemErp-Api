using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Infrastructure.Persistence;
using ERP.Domain.Common;

namespace ERP.Api.Controllers.Common;

public abstract class BaseCrudController<T> : ControllerBase where T : BaseEntity
{
    protected readonly ApplicationDbContext _context;

    protected BaseCrudController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public virtual async Task<IActionResult> GetAll()
    {
        return Ok(await _context.Set<T>().ToListAsync());
    }

    [HttpGet("{id}")]
    public virtual async Task<IActionResult> GetById(Guid id)
    {
        var entity = await _context.Set<T>().FindAsync(id);
        if (entity == null) return NotFound();
        return Ok(entity);
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create(T entity)
    {
        _context.Set<T>().Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }

    [HttpPut("{id}")]
    public virtual async Task<IActionResult> Update(Guid id, T entity)
    {
        if (id != entity.Id) return BadRequest();
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public virtual async Task<IActionResult> Delete(Guid id)
    {
        var entity = await _context.Set<T>().FindAsync(id);
        if (entity == null) return NotFound();
        _context.Set<T>().Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
