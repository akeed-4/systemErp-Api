using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>مواقع توصيل العميل: ترث صلاحية العملاء (<c>master-data</c>).</summary>
[Route("api/v1/customers/{customerId:guid}/delivery-locations"), RequireScreen("master-data")]
public class CustomerDeliveryLocationsController : ErpControllerBase
{
    private readonly ICustomerDeliveryLocationService _locations;
    public CustomerDeliveryLocationsController(ICustomerDeliveryLocationService locations) => _locations = locations;

    [HttpGet, RequireScreen("master-data", ScreenAction.View)]
    public async Task<IActionResult> List(Guid customerId, [FromQuery] bool includeInactive, CancellationToken ct)
        => Success(await _locations.ListAsync(customerId, includeInactive, ct));

    [HttpPost, RequireScreen("master-data", ScreenAction.Create)]
    public async Task<IActionResult> Create(Guid customerId, [FromBody] SaveCustomerDeliveryLocationDto dto, CancellationToken ct)
        => Success(await _locations.CreateAsync(customerId, dto, ct));

    [HttpPut("{id:guid}"), RequireScreen("master-data", ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid customerId, Guid id, [FromBody] SaveCustomerDeliveryLocationDto dto, CancellationToken ct)
        => Success(await _locations.UpdateAsync(customerId, id, dto, ct));

    /// <summary>يعيد <c>deleted=false</c> حين عُطّل الموقع لأنه مستخدم في بيان تسليم.</summary>
    [HttpDelete("{id:guid}"), RequireScreen("master-data", ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid customerId, Guid id, CancellationToken ct)
        => Success(new { deleted = await _locations.DeleteAsync(customerId, id, ct) });
}

/// <summary>البحث الجغرافي عبر وكيل الخادم (أي مستخدم مسجّل؛ يُستخدم في مواقع العملاء والتسليم).</summary>
[Route("api/v1/geocoding")]
public class GeocodingController : ErpControllerBase
{
    private readonly IGeocodingService _geo;
    public GeocodingController(IGeocodingService geo) => _geo = geo;

    [HttpGet("status")] public IActionResult Status() => Success(new { enabled = _geo.Enabled });

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct) => Success(await _geo.SearchAsync(q, ct));
}
