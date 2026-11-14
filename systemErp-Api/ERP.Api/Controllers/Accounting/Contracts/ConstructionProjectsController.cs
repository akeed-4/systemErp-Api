using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Accounting;
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

    /// <summary>تغطية العمالة والمعدات المخططة بالموارد الفعلية المسندة، بالساعات لكل مرحلة (للقراءة).</summary>
    [HttpGet("resource-coverage"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ResourceCoverage(Guid id, [FromServices] IProjectResourceCoverageService coverage, CancellationToken ct)
        => Success(await coverage.GetAsync(id, ct));

    /// <summary>مقاولو الباطن للمشروع: عقود الموردين المرتبطة به بموقف مستخلصاتها، وتغطية بنوده.</summary>
    [HttpGet("subcontracts"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Subcontracts(Guid id, [FromServices] IProjectSubcontractService subcontracts, CancellationToken ct)
        => Success(await subcontracts.GetAsync(id, ct));

    [HttpPut("profile"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveProfile(Guid id, [FromBody] SaveProjectProfileDto dto, CancellationToken ct)
        => Success(await _projects.SaveProfileAsync(id, dto, ct), Messages.ProjectSetupSaved);

    /// <summary>نطاق الحضور الميداني: موقع المشروع ونصف القطر وأضعف دقة مقبولة والتفعيل.</summary>
    [HttpGet("geofence"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Geofence(Guid id, [FromServices] IProjectGeofenceService geofence, CancellationToken ct)
        => Success(await geofence.GetAsync(id, ct));

    [HttpPut("geofence"), RequireScreen(Screen, ScreenAction.Edit)]
    public async Task<IActionResult> SaveGeofence(Guid id, [FromBody] ProjectGeofenceDto dto, [FromServices] IProjectGeofenceService geofence, CancellationToken ct)
        => Success(await geofence.SaveAsync(id, dto, ct), Messages.ProjectGeofenceSaved);

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
    private readonly IRentalAccrualService _rentals;
    private readonly ISiteReportAttachmentService _attachments;

    /// <summary>حد طلب رفع مستند التقرير اليومي: حجم الملف مع هامش لترويسات multipart.</summary>
    private const long MaxAttachmentRequestBytes = SiteReportAttachmentService.MaxBytes + 64 * 1024;

    public SiteDailyReportsController(ISiteDailyReportService reports, IProjectMaterialService materials, IConstructionProjectService projects,
        IRentalAccrualService rentals, ISiteReportAttachmentService attachments)
    {
        _reports = reports; _materials = materials; _projects = projects; _rentals = rentals; _attachments = attachments;
    }

    // ---------- أرشيف مستندات بنود التقرير (مستقل عن دورته؛ صلاحيات الإضافة والحذف بحسب حالته تفحصها الخدمة) ----------

    /// <summary>عدد المستندات لكل بند، وما يملكه المستخدم من إضافة وحذف، وحد الحجم.</summary>
    [HttpGet("{reportId:guid}/attachments/summary"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> AttachmentSummary(Guid id, Guid reportId, CancellationToken ct)
        => Success(await _attachments.SummaryAsync(id, reportId, ct));

    [HttpGet("{reportId:guid}/attachments"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Attachments(Guid id, Guid reportId, [FromQuery] Guid? itemId, CancellationToken ct)
        => Success(await _attachments.ListAsync(id, reportId, itemId, ct));

    [HttpPost("{reportId:guid}/items/{itemId:guid}/attachments"), RequireScreen(Screen, ScreenAction.View)]
    [RequestSizeLimit(MaxAttachmentRequestBytes)]
    public async Task<IActionResult> UploadAttachment(Guid id, Guid reportId, Guid itemId, [FromForm] string? category,
        [FromForm] string? note, IFormFile? file, CancellationToken ct)
    {
        var (name, content) = await LeaveAttachmentUpload.ReadAsync(file, SiteReportAttachmentService.MaxBytes, ct);
        return Success(await _attachments.AddAsync(id, reportId, itemId, category, note, name, content, ct), Messages.CreatedSuccessfully);
    }

    [HttpGet("{reportId:guid}/attachments/{attachmentId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid reportId, Guid attachmentId, CancellationToken ct)
    {
        var file = await _attachments.GetFileAsync(id, reportId, attachmentId, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{reportId:guid}/attachments/{attachmentId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid reportId, Guid attachmentId, CancellationToken ct)
    {
        await _attachments.DeleteAsync(id, reportId, attachmentId, ct);
        return Success(Messages.DeletedSuccessfully);
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

    /// <summary>إعادة فتح التقرير المعتمد للتعديل (صلاحية الاعتماد): القيود باقية، ويُرحَّل الفرق وحده عند إعادة اعتماده.</summary>
    [HttpPost("{reportId:guid}/reopen"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Reopen(Guid id, Guid reportId, [FromBody] SiteReportActionDto dto, CancellationToken ct)
        => Success(await _reports.ReopenAsync(id, reportId, dto, ct), Messages.SiteReportReopened);

    // ---------- استحقاق إيجار المعدات المستأجرة ----------

    [HttpGet("rental-accruals"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> RentalAccruals(Guid id, [FromQuery] bool openOnly, CancellationToken ct)
        => Success(await _rentals.ListAsync(id, openOnly, ct));

    /// <summary>إقفال المتبقي من استحقاق مؤجر (فوترته أقل من المستحق)، بصلاحية الاعتماد وسبب إلزامي.</summary>
    [HttpPost("rental-accruals/close"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> CloseRentalAccruals(Guid id, [FromBody] CloseRentalAccrualDto dto, CancellationToken ct)
    {
        var closed = await _rentals.CloseAsync(id, dto, ct);
        return Success(closed, string.Format(Messages.RentalAccrualClosed, closed.ToString("N2")));
    }

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
