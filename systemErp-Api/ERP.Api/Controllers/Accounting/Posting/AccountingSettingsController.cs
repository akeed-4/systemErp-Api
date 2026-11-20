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

    /// <summary>تهيئة حسابات العقود الناقصة وربطها (لا يغيّر الإعدادات المحددة)؛ يعيد تقرير ما أُنشئ وأُعيد استخدامه وما يحتاج تدخلاً.</summary>
    [HttpPost("contract-accounts")]
    public async Task<IActionResult> InitializeContractAccounts(CancellationToken ct)
        => Success(await _settings.InitializeContractAccountsAsync(ct));

    /// <summary>فحص اكتمال حسابات الجرد الدوري (معاينة بلا كتابة: ما سيُنشأ «ناقص»).</summary>
    [HttpGet("periodic-accounts")]
    public async Task<IActionResult> PeriodicAccountsStatus(CancellationToken ct)
        => Success(await _settings.GetPeriodicAccountsStatusAsync(ct));

    /// <summary>تهيئة حسابات الجرد الدوري الناقصة وربطها (لا يغيّر الإعدادات المحددة)؛ يعيد تقرير النتيجة.</summary>
    [HttpPost("periodic-accounts"), RequireScreen("accounting-settings", ScreenAction.Edit)]
    public async Task<IActionResult> InitializePeriodicAccounts(CancellationToken ct)
        => Success(await _settings.InitializePeriodicAccountsAsync(ct));

    /// <summary>معاينة معالجة قيود الجرد الدوري القديمة التي نقلت رصيد المخزون إلى بضاعة أول المدة.</summary>
    [HttpGet("periodic-legacy")]
    public async Task<IActionResult> PeriodicLegacy(CancellationToken ct)
        => Success(await _settings.GetPeriodicLegacyAsync(ct));

    /// <summary>تنفيذ المعالجة للمنشأة الحالية (يتطلب أيضاً صلاحية ترحيل القيود؛ تُفحص في الخدمة).</summary>
    [HttpPost("periodic-legacy"), RequireScreen("accounting-settings", ScreenAction.Edit)]
    public async Task<IActionResult> ApplyPeriodicLegacy([FromBody] ApplyPeriodicLegacyDto dto, CancellationToken ct)
        => Success(await _settings.ApplyPeriodicLegacyAsync(dto, ct));
}
