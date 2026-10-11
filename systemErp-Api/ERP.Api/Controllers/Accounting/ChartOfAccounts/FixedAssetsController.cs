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
    private readonly IFixedAssetDisposalService _disposal;

    public FixedAssetsController(IFixedAssetService assets, IFixedAssetDepreciationService depreciation, IFixedAssetDisposalService disposal) : base(assets)
    {
        _assets = assets; _depreciation = depreciation; _disposal = disposal;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? costCenterId, [FromQuery] Guid? warehouseId, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _assets.ListAsync(costCenterId, warehouseId, query, ct));

    [NonAction]
    public override Task<IActionResult> List(PaginationParams query, CancellationToken ct) => List(null, null, query, ct);

    /// <summary>سجلات الإهلاك المرحَّلة (period = yyyy-MM).</summary>
    [HttpGet("depreciation")]
    public async Task<IActionResult> Depreciations([FromQuery] Guid? fixedAssetId, [FromQuery] Guid? costCenterId, [FromQuery] Guid? warehouseId,
        [FromQuery] string? period, [FromQuery] PaginationParams query, CancellationToken ct)
        => Success(await _depreciation.ListAsync(fixedAssetId, costCenterId, warehouseId, period, query, ct));

    /// <summary>معاينة إهلاك الفترة: مبلغ وحالة كل أصل دون إنشاء قيد.</summary>
    [HttpPost("depreciation/preview"), RequireScreen("accounts", ScreenAction.View)]
    public async Task<IActionResult> PreviewDepreciation([FromBody] DepreciationRunRequestDto request, CancellationToken ct)
        => Success(await _depreciation.PreviewAsync(request, ct));

    [HttpPost("depreciation/post")]
    public async Task<IActionResult> PostDepreciation([FromBody] DepreciationRunRequestDto request, CancellationToken ct)
        => Success(await _depreciation.PostAsync(request, ct), Messages.DepreciationPosted);

    /// <summary>عكس ترحيل إهلاك بقيده: يعيد أرصدة الأصول ويحذف سجلات الفترة.</summary>
    [HttpPost("depreciation/{journalEntryId:guid}/reverse")]
    public async Task<IActionResult> ReverseDepreciation(Guid journalEntryId, CancellationToken ct)
    {
        await _depreciation.ReverseRunAsync(journalEntryId, ct);
        return Success(Messages.DepreciationReversed);
    }

    /// <summary>استبعاد الأصل (بيع أو شطب) بقيد يثبت ربح أو خسارة الاستبعاد.</summary>
    [HttpPost("{id:guid}/dispose")]
    public async Task<IActionResult> Dispose(Guid id, [FromBody] DisposeFixedAssetDto request, CancellationToken ct)
        => Success(await _disposal.DisposeAsync(id, request, ct), Messages.AssetDisposed);
}
