using ERP.Api.Infrastructure;
using ERP.Core.DTOs.Accounting.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/company")]
public class CompanyController : ErpControllerBase
{
    private readonly ICompanyService _company;
    private readonly ERP.Core.Contracts.Accounting.IZatcaService _zatca;
    public CompanyController(ICompanyService company, ERP.Core.Contracts.Accounting.IZatcaService zatca)
    {
        _company = company; _zatca = zatca;
    }

    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Success(await _company.GetCurrentAsync(ct));

    [HttpPut, RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Update([FromBody] UpdateTenantDto request, CancellationToken ct)
        => Success(await _company.UpdateAsync(request, ct), Messages.CompanyUpdated);

    [HttpPut("ZatcaConfig"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> UpdateZatca([FromBody] UpdateZatcaConfigDto request, CancellationToken ct)
        => Success(await _company.UpdateZatcaConfigAsync(request, ct), Messages.IntegrationSettingsSaved);

    /// <summary>الربط مع هيئة الزكاة برمز التحقق (OTP) من بوابة «فاتورة»: يصدر شهادة الوحدة ويفعّل الإرسال.</summary>
    // بلا صلاحية شاشة: أي مستخدم يربط نفسه (رمز التحقق لا يصدره إلا صاحب حساب المنشأة في بوابة فاتورة)، وربط غيره تحصره الخدمة
    [HttpPost("ZatcaOnboard")]
    public async Task<IActionResult> OnboardZatca([FromBody] ZatcaOnboardRequestDto request, CancellationToken ct)
        => Success(await _zatca.OnboardAsync(request, ct), Messages.ZatcaOnboarded);

    [HttpGet("ZatcaOnboard"), RequireScreen("zatca", ScreenAction.View)]
    public async Task<IActionResult> ZatcaOnboardingStatus([FromQuery] Guid? userId, CancellationToken ct)
        => Success(await _zatca.GetOnboardingStatusAsync(userId, ct));

    [HttpPost("ZatcaOnboard/{sessionId:guid}/tests/{testId:guid}/retry"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> RetryZatcaComplianceTest(Guid sessionId, Guid testId, CancellationToken ct)
        => Success(await _zatca.RetryComplianceTestAsync(sessionId, testId, ct));

    /// <summary>فحص اتصال الخادم ببوابة الهيئة: DNS وTCP وTLS وطلب HTTPS لا يصدر شهادة، مع الزمن وتفاصيل الخطأ. للمالك ومدير النظام.</summary>
    [HttpGet("ZatcaConnectivity"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> ZatcaConnectivity([FromQuery] ZatcaEnvironment? environment, CancellationToken ct)
        => Success(await _zatca.CheckConnectivityAsync(environment, ct));

    /// <summary>جلسات الربط العالقة (طلب شهادة جارٍ أو مجهول المصير) وإمكانية معالجة كل منها. للمالك ومدير النظام.</summary>
    [HttpGet("ZatcaOnboard/stuck"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> ZatcaStuckSessions(CancellationToken ct) => Success(await _zatca.ListStuckSessionsAsync(ct));

    /// <summary>إغلاق جلسة امتثال عالقة لم تُحفظ لها شهادة (تبقى للتدقيق) للسماح بمحاولة جديدة آمنة. للمالك ومدير النظام.</summary>
    [HttpPost("ZatcaOnboard/{sessionId:guid}/recover"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> RecoverZatcaSession(Guid sessionId, [FromBody] ZatcaSessionRecoveryRequestDto request, CancellationToken ct)
        => Success(await _zatca.RecoverStuckSessionAsync(sessionId, request, ct));

    /// <summary>المستخدمون المربوطون مع الهيئة (كلهم للمالك ومدير النظام، والمستخدم نفسه لغيرهما).</summary>
    [HttpGet("ZatcaUnits")]
    public async Task<IActionResult> ZatcaUnits(CancellationToken ct) => Success(await _zatca.ListUnitsAsync(ct));

    [HttpDelete("ZatcaUnits/{id:guid}")]
    public async Task<IActionResult> DisableZatcaUnit(Guid id, CancellationToken ct)
    {
        await _zatca.DisableUnitAsync(id, ct);
        return Success(Messages.ZatcaUnitDisabled);
    }

    [HttpPost("ZatcaTest"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> TestZatca(CancellationToken ct) => Success(await _company.TestZatcaConnectionAsync(ct));
}
