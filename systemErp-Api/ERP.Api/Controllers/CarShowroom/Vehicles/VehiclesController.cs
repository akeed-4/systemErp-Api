using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/vehicles"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class VehiclesController : CrudController<VehicleDto, CreateVehicleDto, UpdateVehicleDto>
{
    private readonly IVehicleService _vehicles;
    private readonly IVehicleCostService _costs;
    public VehiclesController(IVehicleService s, IVehicleCostService costs) : base(s)
    {
        _vehicles = s; _costs = costs;
    }

    /// <summary>كرت تكلفة السيارة: ثمن الشراء وكل مصروف سُجّل عليها، المتوقع مقابل الفعلي. لمن يملك صلاحية رؤية التكلفة.</summary>
    [HttpGet("{id:guid}/cost-card")]
    [RequireScreen(PermissionService.CarCostViewScreenId, ScreenAction.View)]
    public async Task<IActionResult> CostCard(Guid id, CancellationToken ct) => Success(await _costs.GetCostCardAsync(id, ct));

    /// <summary>قيود التعديل الموثقة على تكلفة السيارة بعد بيعها (مصاريف وصلت بعد البيع وحُمّلت على تكلفة المبيعات).</summary>
    [HttpGet("{id:guid}/cost-adjustments")]
    [RequireScreen(PermissionService.CarCostViewScreenId, ScreenAction.View)]
    public async Task<IActionResult> CostAdjustments(Guid id, CancellationToken ct) => Success(await _costs.GetAdjustmentsAsync(id, ct));

    /// <summary>يحوّل أرصدة حقول التكلفة القديمة لمركبات المنشأة إلى أسطر «رصيد افتتاحي». آمن للتكرار.</summary>
    [HttpPost("MigrateLegacyCosts")]
    [RequireScreen("car-showroom", ScreenAction.Approve)]
    public async Task<IActionResult> MigrateLegacyCosts(CancellationToken ct)
    {
        var created = await _costs.MigrateLegacyCostsAsync(ct);
        return Success(new { created }, string.Format(Messages.LegacyCostsMigrated, created));
    }

    [HttpGet("available")]
    public async Task<IActionResult> Available([FromQuery] string? search, CancellationToken ct) => Success(await _vehicles.ListAvailableAsync(search, ct));

    [HttpPost("CalculateVat")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> CalculateVat([FromBody] CalculateCarVatRequestDto request, CancellationToken ct)
        => Success(await _vehicles.CalculateVatAsync(request, ct));

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] List<CreateVehicleDto> items, CancellationToken ct)
        => Success(await _vehicles.ImportAsync(items, ct));

    /// <summary>توليد أرقام شاسيه مؤقتة فريدة لسيارات فاتورة الشراء.</summary>
    [HttpPost("GenerateVins")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> GenerateVins([FromBody] GenerateVinsRequestDto request, CancellationToken ct)
        => Success(await _vehicles.GenerateProvisionalVinsAsync(request.Count, ct));

    [HttpPost("CheckVins")]
    [RequireScreen("car-showroom", ScreenAction.View)]
    public async Task<IActionResult> CheckVins([FromBody] List<string> vins, CancellationToken ct)
        => Success(await _vehicles.FindExistingChassisNumbersAsync(vins, ct));
}
