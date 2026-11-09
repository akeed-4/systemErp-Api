using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// إعداد مشروع المقاولات (العقد هو المشروع): الملف وساعات العمل، والمراحل، والأعضاء. المسار تحت سجل العقد فيخضع لنطاق الوصول
/// على العقد (المشرف بنطاق «المسندة إليه» لا يصل إلا لمشاريعه). الشاشة «مشاريع المقاولات» (ترث صلاحية العقود).
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}/project")]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ConstructionProjectsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.ConstructionProjectsScreenId;
    private readonly IConstructionProjectService _projects;

    public ConstructionProjectsController(IConstructionProjectService projects) => _projects = projects;

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _projects.GetAsync(id, ct));

    /// <summary>ملخص المشروع للوحة المشروع: الإنجاز والتكلفة والحالات والتنبيهات.</summary>
    [HttpGet("summary"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Summary(Guid id, CancellationToken ct) => Success(await _projects.SummaryAsync(id, ct));

    /// <summary>تكلفة المشروع وربحيته من القيود على مركز تكلفته مقابل المخطط، مع التوقع عند الاكتمال.</summary>
    [HttpGet("cost-report"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CostReport(Guid id, [FromServices] IProjectCostService costs, CancellationToken ct)
        => Success(await costs.ReportAsync(id, ct));

    /// <summary>مقاولو الباطن للمشروع: عقود الموردين المرتبطة به بموقف مستخلصاتها، وتغطية بنوده.</summary>
    [HttpGet("subcontracts"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Subcontracts(Guid id, [FromServices] IProjectSubcontractService subcontracts, CancellationToken ct)
        => Success(await subcontracts.GetAsync(id, ct));

    [HttpPut("profile"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveProfile(Guid id, [FromBody] SaveProjectProfileDto dto, CancellationToken ct)
        => Success(await _projects.SaveProfileAsync(id, dto, ct), Messages.ProjectSetupSaved);

    [HttpPut("phases"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SavePhases(Guid id, [FromBody] SaveProjectPhasesDto dto, CancellationToken ct)
        => Success(await _projects.SavePhasesAsync(id, dto, ct), Messages.ProjectSetupSaved);

    [HttpGet("member-candidates"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> MemberCandidates(Guid id, CancellationToken ct) => Success(await _projects.MemberCandidatesAsync(ct));

    [HttpPost("members"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] SaveProjectMemberDto dto, CancellationToken ct)
        => Success(await _projects.AddMemberAsync(id, dto, ct), Messages.ProjectSetupSaved);

    [HttpPut("members/{memberId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateMember(Guid id, Guid memberId, [FromBody] SaveProjectMemberDto dto, CancellationToken ct)
        => Success(await _projects.UpdateMemberAsync(id, memberId, dto, ct), Messages.ProjectSetupSaved);

    [HttpDelete("members/{memberId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken ct)
        => Success(await _projects.RemoveMemberAsync(id, memberId, ct), Messages.ProjectSetupSaved);

    // ---------- الموارد وتكلفة الساعة ----------

    [HttpGet("resources"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Resources(Guid id, CancellationToken ct) => Success(await _projects.ResourcesAsync(id, ct));

    [HttpPost("resources"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> AddResource(Guid id, [FromBody] ProjectResourceDto dto, CancellationToken ct)
        => Success(await _projects.SaveResourceAsync(id, null, dto, ct), Messages.ProjectSetupSaved);

    [HttpPut("resources/{resourceId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> UpdateResource(Guid id, Guid resourceId, [FromBody] ProjectResourceDto dto, CancellationToken ct)
        => Success(await _projects.SaveResourceAsync(id, resourceId, dto, ct), Messages.ProjectSetupSaved);

    [HttpDelete("resources/{resourceId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> DeleteResource(Guid id, Guid resourceId, CancellationToken ct)
        => Success(await _projects.DeleteResourceAsync(id, resourceId, ct), Messages.ProjectSetupSaved);

    // ---------- خطة الإنجاز اليومية ----------

    [HttpGet("daily-plans"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DailyPlans(Guid id, CancellationToken ct) => Success(await _projects.DailyPlansAsync(id, ct));

    [HttpPut("daily-plans"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveDailyPlans(Guid id, [FromBody] SaveProjectDailyPlansDto dto, CancellationToken ct)
        => Success(await _projects.SaveDailyPlansAsync(id, dto, ct), Messages.ProjectSetupSaved);
}

/// <summary>
/// التقرير اليومي للموقع (الإنجاز بالساعة) تحت سجل العقد فيخضع لنطاقه (المشرف يصل لمشاريعه). شاشة «التقرير اليومي للموقع»:
/// الإضافة والتعديل والتقديم للمشرف، والاعتماد والإعادة للمهندس المسؤول (صلاحية الاعتماد).
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}/site-reports")]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class SiteDailyReportsController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.SiteDailyReportsScreenId;
    private readonly ISiteDailyReportService _reports;
    private readonly IProjectMaterialService _materials;
    private readonly IConstructionProjectService _projects;

    public SiteDailyReportsController(ISiteDailyReportService reports, IProjectMaterialService materials, IConstructionProjectService projects)
    {
        _reports = reports; _materials = materials; _projects = projects;
    }

    // بيانات التقرير بصلاحيته وحدها (مشرف الموقع لا يحتاج صلاحية العقود أو المشاريع): أسطر جدول الكميات، وموارد المشروع

    [HttpGet("lines"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Lines(Guid id, CancellationToken ct) => Success(await _reports.BoqLinesAsync(id, ct));

    [HttpGet("project-resources"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ProjectResources(Guid id, CancellationToken ct) => Success(await _projects.ResourcesAsync(id, ct));

    /// <summary>مواد المكتبة لبند في التقرير: تحليل البند، أو المكتبة عامة لبند غير مرتبط أو مع all.</summary>
    [HttpGet("material-options"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> MaterialOptions(Guid id, [FromQuery] Guid itemId, [FromQuery] bool all, CancellationToken ct)
        => Success(await _materials.CandidatesAsync(id, itemId, all, ct));

    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> List(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _reports.ListAsync(id, from, to, ct));

    /// <summary>تقرير اليوم أو قالبه الجديد من ساعات العمل وخطة الإنجاز.</summary>
    [HttpGet("day"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Day(Guid id, [FromQuery] DateTime date, CancellationToken ct) => Success(await _reports.DayAsync(id, date, ct));

    [HttpGet("{reportId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Get(Guid id, Guid reportId, CancellationToken ct) => Success(await _reports.GetAsync(id, reportId, ct));

    [HttpPost, RequireScreen(Screen, ScreenAction.Create)]
    public async Task<IActionResult> Create(Guid id, [FromBody] SaveSiteDailyReportDto dto, CancellationToken ct)
        => Success(await _reports.CreateAsync(id, dto, ct), Messages.SiteReportSaved);

    [HttpPut("{reportId:guid}"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> Update(Guid id, Guid reportId, [FromBody] SaveSiteDailyReportDto dto, CancellationToken ct)
        => Success(await _reports.UpdateAsync(id, reportId, dto, ct), Messages.SiteReportSaved);

    [HttpDelete("{reportId:guid}"), RequireScreen(Screen, ScreenAction.Delete)]
    public async Task<IActionResult> Delete(Guid id, Guid reportId, CancellationToken ct)
    {
        await _reports.DeleteAsync(id, reportId, ct);
        return Success(true);
    }

    [HttpPost("{reportId:guid}/submit"), RequireScreen(Screen, ScreenAction.Submit)]
    public async Task<IActionResult> Submit(Guid id, Guid reportId, CancellationToken ct)
        => Success(await _reports.SubmitAsync(id, reportId, ct), Messages.SiteReportSubmitted);

    [HttpPost("{reportId:guid}/approve"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, Guid reportId, CancellationToken ct)
        => Success(await _reports.ApproveAsync(id, reportId, ct), Messages.SiteReportApproved);

    /// <summary>ترحيل قيد تحميل العمالة والمعدات للتقارير المعتمدة قبل تفعيله (صلاحية الاعتماد).</summary>
    [HttpPost("post-costs"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> PostPendingCosts(Guid id, CancellationToken ct)
        => Success(await _reports.PostPendingCostsAsync(id, ct));

    [HttpPost("{reportId:guid}/return"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Return(Guid id, Guid reportId, [FromBody] SiteReportActionDto dto, CancellationToken ct)
        => Success(await _reports.ReturnAsync(id, reportId, dto, ct), Messages.SiteReportReturned);

    // ---------- تقارير الإنجاز (المعتمد فقط ما لم يُطلب غيره) ----------

    [HttpGet("hourly"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Hourly(Guid id, [FromQuery] SiteReportFilterDto filter, CancellationToken ct)
        => Success(await _reports.HourlyAsync(id, filter, ct));

    [HttpGet("productivity"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Productivity(Guid id, [FromQuery] SiteReportFilterDto filter, CancellationToken ct)
        => Success(await _reports.ProductivityAsync(id, filter, ct));

    [HttpGet("resources"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ResourceUsage(Guid id, [FromQuery] SiteReportFilterDto filter, CancellationToken ct)
        => Success(await _reports.ResourceUsageAsync(id, filter, ct));
}

/// <summary>
/// مشاريع المقاولات خارج سجل عقد: قائمة المشاريع بملخصها (في نطاق وصول المستخدم إلى العقود)، وأدوار المقاولات الجاهزة
/// (مدير المشروع، المهندس المشرف، مراقب التكاليف، ...) وإنشاؤها إداري كإنشاء أي دور.
/// </summary>
[Route("api/v1/construction-projects")]
[RequireModule(PlatformModules.Construction)]
// القائمة (GET) تُرشَّح بنطاق الوصول إلى العقود: المشرف يرى مشاريعه
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ConstructionProjectsIndexController : ErpControllerBase
{
    private readonly IConstructionProjectService _projects;

    public ConstructionProjectsIndexController(IConstructionProjectService projects) => _projects = projects;

    [HttpGet, RequireScreen(PermissionCatalog.ConstructionProjectsScreenId, ScreenAction.View)]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _projects.ListAsync(ct));

    /// <summary>مشاريع التقرير اليومي بصلاحيته وحدها (في نطاق المستخدم): الهوية فقط بلا أرقام مالية.</summary>
    [HttpGet("site-report-projects"), RequireScreen(PermissionCatalog.SiteDailyReportsScreenId, ScreenAction.View)]
    public async Task<IActionResult> SiteReportProjects(CancellationToken ct)
        => Success((await _projects.ListAsync(ct)).Select(p => new SiteReportProjectDto
        {
            ContractId = p.ContractId, ContractNumber = p.ContractNumber, Title = p.Title, ProjectName = p.ProjectName, ProjectLocation = p.ProjectLocation,
        }).ToList());

    [HttpPost("role-templates"), RequireScreen("user-permissions", ScreenAction.Create)]
    public async Task<IActionResult> CreateRoleTemplates(CancellationToken ct) => Success(await _projects.CreateRoleTemplatesAsync(ct));
}
