using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>إعدادات الحسابات المحاسبية وطريقة الجرد (شاشة موحدة).</summary>
[Route("api/v1/accounting-settings"), RequireScreen("accounting-settings")]
public class AccountingSettingsController : ErpControllerBase
{
    private readonly IAccountingSettingsService _settings;
    public AccountingSettingsController(IAccountingSettingsService settings) => _settings = settings;

    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Success(await _settings.GetAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SaveAccountingSettingsDto dto, CancellationToken ct)
        => Success(await _settings.SaveAsync(dto, ct), Messages.AccountingSettingsSaved);
}
