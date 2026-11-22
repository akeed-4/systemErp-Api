using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/warehouses"), RequireScreen("warehouses")]
public class WarehousesController : CrudController<WarehouseDto, CreateWarehouseDto, UpdateWarehouseDto>
{
    private readonly IWarehouseService _warehouses;
    public WarehousesController(IWarehouseService s) : base(s) => _warehouses = s;

    /// <summary>حسابات المواقع القائمة: ينشئ الناقص منها (الجديدة تُنشأ مع الموقع تلقائياً).</summary>
    [HttpPost("site-accounts"), RequireScreen("warehouses", ScreenAction.Edit)]
    public async Task<IActionResult> EnsureSiteAccounts(CancellationToken ct) => Success(await _warehouses.EnsureSiteAccountsAsync(ct));
}
