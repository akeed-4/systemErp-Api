using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/fixedassets"), RequireScreen("accounts")]
public class FixedAssetsController : CrudController<FixedAssetDto, CreateFixedAssetDto, UpdateFixedAssetDto>
{
    private readonly IFixedAssetService _assets;
    private readonly IFixedAssetDepreciationService _depreciation;

    public FixedAssetsController(IFixedAssetService assets, IFixedAssetDepreciationService depreciation) : base(assets)
    {
        _assets = assets; _depreciation = depreciation;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? costCenterId, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _assets.ListAsync(costCenterId, query, ct));

    [NonAction]
    public override Task<IActionResult> List(PaginationParams query, CancellationToken ct) => List(null, query, ct);

    /// <summary>سجلات الإهلاك المرحَّلة (period = yyyy-MM).</summary>
    [HttpGet("depreciation")]
    public async Task<IActionResult> Depreciations([FromQuery] Guid? fixedAssetId, [FromQuery] Guid? costCenterId, [FromQuery] string? period,
        [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _depreciation.ListAsync(fixedAssetId, costCenterId, period, query, ct));

    /// <summary>معاينة إهلاك الفترة: مبلغ وحالة كل أصل دون إنشاء قيد.</summary>
    [HttpPost("depreciation/preview"), RequireScreen("accounts", ScreenAction.View)]
    public async Task<IActionResult> PreviewDepreciation([FromBody] DepreciationRunRequestDto request, CancellationToken ct)
        => Success(await _depreciation.PreviewAsync(request, ct));

    [HttpPost("depreciation/post")]
    public async Task<IActionResult> PostDepreciation([FromBody] DepreciationRunRequestDto request, CancellationToken ct)
        => Success(await _depreciation.PostAsync(request, ct), Messages.DepreciationPosted);
}
