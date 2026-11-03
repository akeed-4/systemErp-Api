using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

/// <summary>قواعد الخصومات وبرنامج الولاء في نقاط البيع (سجل واحد للمنشأة).</summary>
[Route("api/v1/pos/promotion-settings"), RequireScreen("sales")]
[RequireModule(PlatformModules.Pos)]
public class PosPromotionSettingsController : ErpControllerBase
{
    private readonly IPosPromotionSettingsService _settings;
    public PosPromotionSettingsController(IPosPromotionSettingsService settings) => _settings = settings;

    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Success(await _settings.GetAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePosPromotionSettingsDto dto, CancellationToken ct)
        => Success(await _settings.UpdateAsync(dto, ct), Messages.PromotionSettingsSaved);
}
