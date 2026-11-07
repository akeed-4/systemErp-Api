using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>مكتبة البنود (المقاولات): البند الأساسي ببنوده الفرعية واحتياجاته؛ شاشة «مكتبة المقاولات».</summary>
[Route("api/v1/work-items")]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class WorkItemsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.ConstructionLibraryScreenId;
    private readonly IWorkItemService _items;

    public WorkItemsController(IWorkItemService items) => _items = items;

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _items.ListAsync(ct));

    [HttpGet("{id:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _items.GetAsync(id, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create([FromBody] SaveWorkItemDto dto, CancellationToken ct) => Success(await _items.CreateAsync(dto, ct));

    [HttpPut("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveWorkItemDto dto, CancellationToken ct) => Success(await _items.UpdateAsync(id, dto, ct));

    [HttpDelete("{id:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _items.DeleteAsync(id, ct);
        return Success(true);
    }
}
