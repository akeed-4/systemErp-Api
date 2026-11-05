using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// أنواع العقود للمنشأة (شاشة «أنواع العقود»، ترث صلاحية العقود): المسار وطريقة الفوترة وحد التبسيط.
/// قائمة الأنواع الفعّالة لنموذج العقد في <c>commercialcontracts/types</c> بصلاحية العقود.
/// </summary>
[Route("api/v1/contract-types"), RequireScreen(PermissionCatalog.ContractTypesScreenId)]
[RequireModule(PlatformModules.Accounting)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ContractTypesController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.ContractTypesScreenId;
    private readonly IContractTypeService _types;

    public ContractTypesController(IContractTypeService types) => _types = types;

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _types.ListAsync(activeOnly: false, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Create([FromBody] SaveContractTypeDto dto, CancellationToken ct)
        => Success(await _types.CreateAsync(dto, ct), Messages.CreatedSuccessfully);

    [HttpPut("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveContractTypeDto dto, CancellationToken ct)
        => Success(await _types.UpdateAsync(id, dto, ct), Messages.UpdatedSuccessfully);

    [HttpDelete("{id:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _types.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
