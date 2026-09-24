using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ERP.Api.Infrastructure;

/// <summary>CRUD موحّد فوق ICrudService. المسار والصلاحية يحدّدهما الصنف المشتق.</summary>
public abstract class CrudController<TDto, TCreate, TUpdate> : ErpControllerBase
{
    private readonly ICrudService<TDto, TCreate, TUpdate> _service;
    protected CrudController(ICrudService<TDto, TCreate, TUpdate> service) => _service = service;

    [HttpGet]
    public virtual async Task<IActionResult> List([FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _service.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public virtual async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TCreate dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        var id = created?.GetType().GetProperty("Id")?.GetValue(created);
        return Created($"{Request.Path}/{id}", ApiResponse<TDto>.Ok(created!, "تم الإنشاء بنجاح") .WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public virtual async Task<IActionResult> Update(Guid id, [FromBody] TUpdate dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), "تم التعديل بنجاح");

    [HttpDelete("{id:guid}")]
    public virtual async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return Success("تم الحذف بنجاح");
    }
}
