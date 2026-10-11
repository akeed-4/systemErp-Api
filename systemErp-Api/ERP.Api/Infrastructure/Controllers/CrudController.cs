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

    /// <summary>
    /// القائمة بخيارات DevExtreme (filter/sort/skip/take/totalSummary/requireTotalCount) كما يرسلها DataSource/CustomStore،
    /// وتُعيد <c>LoadResult</c> مباشرة ({ data, totalCount, summary }) دون مغلّف ApiResponse.
    /// </summary>
    [HttpGet("load")]
    public virtual async Task<IActionResult> Load(DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _service.LoadAsync(loadOptions, ct));

    [HttpGet("{id:guid}")]
    public virtual async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Success(await _service.GetAsync(id, ct));

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TCreate dto, CancellationToken ct)
    {
        var created = await _service.CreateAsync(dto, ct);
        var id = created?.GetType().GetProperty("Id")?.GetValue(created);
        return Created($"{Request.Path}/{id}", ApiResponse<TDto>.Ok(created!, Messages.CreatedSuccessfully) .WithStatus(201));
    }

    [HttpPut("{id:guid}")]
    public virtual async Task<IActionResult> Update(Guid id, [FromBody] TUpdate dto, CancellationToken ct)
        => Success(await _service.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);

    [HttpDelete("{id:guid}")]
    public virtual async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
