using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// مواقع المستخدم (المعارض/المستودعات) وموقع الجلسة: تختار الواجهة الموقع عند الدخول وترسله في ترويسة X-Warehouse-Id؛
/// وإعداد المنشأة «المستندات حسب المعرض / حسب المستخدم».
/// </summary>
[Route("api/v1/warehouse-session")]
public class WarehouseSessionController : ErpControllerBase
{
    private readonly IWarehouseSessionService _warehouses;
    public WarehouseSessionController(IWarehouseSessionService warehouses) => _warehouses = warehouses;

    [HttpGet]
    public async Task<IActionResult> Session(CancellationToken ct) => Success(await _warehouses.GetAsync(ct));

    /// <summary>ترقيم المستندات للمنشأة أو لكل موقع أو لكل مستخدم.</summary>
    [HttpPut("numbering-scope"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> SetNumberingScope([FromBody] NumberingScopeRequestDto dto, CancellationToken ct)
        => Success(await _warehouses.SetNumberingScopeAsync(dto.DocumentNumberingScope, ct), Messages.UpdatedSuccessfully);

    [HttpPut("document-visibility"), RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> SetVisibility([FromBody] DocumentVisibilityRequestDto dto, CancellationToken ct)
        => Success(await _warehouses.SetDocumentVisibilityAsync(dto.DocumentVisibility, ct), Messages.UpdatedSuccessfully);
}
