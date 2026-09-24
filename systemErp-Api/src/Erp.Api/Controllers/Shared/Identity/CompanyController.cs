using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/company")]
public class CompanyController : ErpControllerBase
{
    private readonly ICompanyService _company;
    public CompanyController(ICompanyService company) => _company = company;

    [HttpGet] public async Task<IActionResult> Get(CancellationToken ct) => Success(await _company.GetCurrentAsync(ct));

    [HttpPut, RequireScreen("user-permissions", ScreenAction.Edit)]
    public async Task<IActionResult> Update([FromBody] UpdateTenantDto request, CancellationToken ct)
        => Success(await _company.UpdateAsync(request, ct), "تم تحديث بيانات المنشأة");

    [HttpPut("zatca-config"), RequireScreen("zatca")]
    public async Task<IActionResult> UpdateZatca([FromBody] UpdateZatcaConfigDto request, CancellationToken ct)
        => Success(await _company.UpdateZatcaConfigAsync(request, ct), "تم حفظ إعدادات الربط");

    [HttpPost("zatca-test"), RequireScreen("zatca", ScreenAction.Edit)]
    public async Task<IActionResult> TestZatca(CancellationToken ct) => Success(await _company.TestZatcaConnectionAsync(ct));
}
