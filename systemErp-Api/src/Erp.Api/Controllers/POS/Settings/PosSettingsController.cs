using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/settings"), RequireScreen("sales")]
public class PosSettingsController : ErpControllerBase
{
    private readonly IPosSettingsService _settings;
    public PosSettingsController(IPosSettingsService settings) => _settings = settings;

    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Success(await _settings.GetAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePosInvoiceSettingsDto dto, CancellationToken ct)
        => Success(await _settings.UpdateAsync(dto, ct), "تم حفظ إعدادات الفاتورة");
}
