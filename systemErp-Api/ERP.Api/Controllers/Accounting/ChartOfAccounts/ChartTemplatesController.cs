using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>قوالب الدليل المحاسبي والأنشطة (لوحة مدير المنصة، على القاعدة المشتركة). قائمة الأنشطة الفعّالة وحدها عامة لنموذج التسجيل.</summary>
[Route("api/v1/platform/chart-templates")]
public class ChartTemplatesController : ErpControllerBase
{
    private readonly IChartTemplateService _templates;
    public ChartTemplatesController(IChartTemplateService templates) => _templates = templates;

    [HttpGet, RequirePlatformAdmin]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _templates.ListTemplatesAsync(ct));

    [HttpGet("{id:guid}"), RequirePlatformAdmin]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _templates.GetTemplateAsync(id, ct));

    [HttpPost, RequirePlatformAdmin]
    public async Task<IActionResult> Create([FromBody] SaveChartTemplateDto dto, CancellationToken ct)
        => Success(await _templates.CreateTemplateAsync(dto, ct), Messages.ChartTemplateSaved);

    [HttpPut("{id:guid}"), RequirePlatformAdmin]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveChartTemplateDto dto, CancellationToken ct)
        => Success(await _templates.UpdateTemplateAsync(id, dto, ct), Messages.ChartTemplateSaved);

    [HttpDelete("{id:guid}"), RequirePlatformAdmin]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _templates.DeleteTemplateAsync(id, ct);
        return Success(Messages.ChartTemplateSaved);
    }

    /// <summary>الشجرة الناتجة لنشاط وموديولات (منشأة جديدة) قبل اعتمادها، مع أخطاء الدمج.</summary>
    [HttpPost("preview"), RequirePlatformAdmin]
    public async Task<IActionResult> Preview([FromBody] ChartCompositionRequestDto request, CancellationToken ct)
        => Success(await _templates.PreviewAsync(request, ct));

    [HttpGet("semantic-keys"), RequirePlatformAdmin]
    public IActionResult SemanticKeys() => Success(_templates.SemanticKeys());

    [HttpGet("activities"), RequirePlatformAdmin]
    public async Task<IActionResult> Activities(CancellationToken ct) => Success(await _templates.ListActivitiesAsync(false, ct));

    [HttpPut("activities"), RequirePlatformAdmin]
    public async Task<IActionResult> SaveActivity([FromBody] BusinessActivityDto dto, CancellationToken ct)
        => Success(await _templates.SaveActivityAsync(dto, ct));
}

/// <summary>الأنشطة الفعّالة لقائمة النشاط في التسجيل العام (بلا دخول).</summary>
[Route("api/v1/business-activities")]
public class BusinessActivitiesController : ErpControllerBase
{
    private readonly IChartTemplateService _templates;
    public BusinessActivitiesController(IChartTemplateService templates) => _templates = templates;

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _templates.ListActivitiesAsync(true, ct));
}

/// <summary>دليل المنشأة الحالية من القوالب: الحالة والمعاينة والتطبيق المعتمد والربط الوظيفي (شاشة إعدادات الحسابات).</summary>
[Route("api/v1/chart-setup"), RequireScreen("accounting-settings")]
public class ChartSetupController : ErpControllerBase
{
    private readonly IChartSetupService _setup;
    public ChartSetupController(IChartSetupService setup) => _setup = setup;

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Success(await _setup.GetStatusAsync(ct));

    /// <summary>ما سيُنشأ أو يُربط (بلا كتابة): الموديولات الناقصة، أو قالب نشاط جديد للمراجعة.</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] ApplyChartSetupDto request, CancellationToken ct)
        => Success(await _setup.PreviewAsync(request, ct));

    /// <summary>تطبيق معتمد: الناقص فقط في معاملة واحدة؛ لا يعدّل حساباً قائماً ولا يحذفه.</summary>
    [HttpPost("apply"), RequireScreen("accounting-settings", ScreenAction.Edit)]
    public async Task<IActionResult> Apply([FromBody] ApplyChartSetupDto request, CancellationToken ct)
        => Success(await _setup.ApplyAsync(request, ct), Messages.ChartSetupApplied);

    [HttpGet("unused-module-accounts")]
    public async Task<IActionResult> UnusedModuleAccounts(CancellationToken ct) => Success(await _setup.UnusedModuleAccountsAsync(ct));

    [HttpGet("bindings")]
    public async Task<IActionResult> Bindings(CancellationToken ct) => Success(await _setup.ListBindingsAsync(ct));

    [HttpPut("bindings"), RequireScreen("accounting-settings", ScreenAction.Edit)]
    public async Task<IActionResult> SaveBinding([FromBody] SaveAccountBindingDto dto, CancellationToken ct)
        => Success(await _setup.SaveBindingAsync(dto, ct));
}
