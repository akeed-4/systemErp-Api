using ERP.Api.Infrastructure;
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

    [HttpPut("ZatcaConfig"), RequireScreen("zatca")]
    public async Task<IActionResult> UpdateZatca([FromBody] UpdateZatcaConfigDto request, CancellationToken ct)
        => Success(await _company.UpdateZatcaConfigAsync(request, ct), Messages.IntegrationSettingsSaved);

    /// <summary>الربط مع هيئة الزكاة برمز التحقق (OTP) من بوابة «فاتورة»: يصدر شهادة الوحدة ويفعّل الإرسال.</summary>
    // بلا صلاحية شاشة: أي مستخدم يربط نفسه (رمز التحقق لا يصدره إلا صاحب حساب المنشأة في بوابة فاتورة)، وربط غيره تحصره الخدمة
    [HttpPost("ZatcaOnboard")]
    public async Task<IActionResult> OnboardZatca([FromBody] ZatcaOnboardRequestDto request, CancellationToken ct)
        => Success(await _zatca.OnboardAsync(request, ct), Messages.ZatcaOnboarded);

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
