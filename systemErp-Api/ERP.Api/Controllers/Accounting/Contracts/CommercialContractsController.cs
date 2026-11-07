using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// العقود التجارية: شاشة صلاحيات مستقلة ونطاق وصول على سجلاتها. تغيير الحالة عبر <c>move</c> وحدها (دالة الانتقال)
/// بانتقالات جدول ContractTransition وصلاحياتها وشروطها؛ والمراجعة القانونية/المالية والتوقيع والضمان والمرفقات من مسارات مستقلة.
/// </summary>
[Route("api/v1/commercialcontracts"), RequireScreen(PermissionCatalog.CommercialContractsScreenId)]
[RequireModule(PlatformModules.Accounting)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class CommercialContractsController : CrudController<CommercialContractDto, CreateCommercialContractDto, UpdateCommercialContractDto>
{
    private const string Screen = PermissionCatalog.CommercialContractsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;
    private readonly ICommercialContractService _contracts;
    private readonly IContractWorkflowService _workflow;
    private readonly IContractCertificateService _certificates;
    private readonly IContractBillingService _billing;
    private readonly IPermissionService _permissions;
    private readonly IProjectMaterialService _materials;

    public CommercialContractsController(ICommercialContractService s, IContractWorkflowService workflow, IContractCertificateService certificates,
        IContractBillingService billing, IPermissionService permissions, IProjectMaterialService materials) : base(s)
    {
        _contracts = s; _workflow = workflow; _certificates = certificates; _billing = billing; _permissions = permissions; _materials = materials;
    }

    // ---------- احتياجات مواد المشروع (العقد هو المشروع): التعديل للإعداد والتقديم، والاعتماد للاعتماد والإعادة ----------

    private const string MaterialsApproval = "contract-material-requirements";

    [HttpGet("{id:guid}/material-requirements"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> MaterialRequirements(Guid id, CancellationToken ct)
        => Success(await _materials.GetAsync(id, ct));

    /// <summary>معاينة الاحتياجات من جدول الكميات وتحليل بنود المكتبة (لا تُحفظ).</summary>
    [HttpPost("{id:guid}/material-requirements/derive"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DeriveMaterialRequirements(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return Success(await _materials.DeriveAsync(id, ct));
    }

    [HttpPut("{id:guid}/material-requirements"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> SaveMaterialRequirements(Guid id, [FromBody] SaveProjectMaterialPlanDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return Success(await _materials.SaveAsync(id, dto, ct), Messages.ProjectMaterialPlanSaved);
    }

    [HttpPost("{id:guid}/material-requirements/submit"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> SubmitMaterialRequirements(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return Success(await _materials.SubmitAsync(id, ct), Messages.ProjectMaterialPlanSubmitted);
    }

    /// <summary>الاعتماد يمر بسياسة الاعتماد إن وُجدت (يُحجز بطلب اعتماد ويُنفَّذ عند الاعتماد النهائي).</summary>
    [HttpPost("{id:guid}/material-requirements/approve"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ApproveMaterialRequirements(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        if (await HoldForApprovalAsync(MaterialsApproval, ApprovalDocumentCatalog.Approve, id, null, ct) is { } held) return held;
        return Success(await _materials.ApproveAsync(id, ct), Messages.ProjectMaterialPlanApproved);
    }

    [HttpPost("{id:guid}/material-requirements/return"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ReturnMaterialRequirements(Guid id, [FromBody] ProjectMaterialPlanActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        return Success(await _materials.ReturnAsync(id, dto, ct), Messages.ProjectMaterialPlanReturned);
    }

    protected override string? ApprovalHandler => "commercial-contracts";

    [HttpGet("stages")]
    public async Task<IActionResult> GetStages(CancellationToken ct)
        => Success(await _contracts.GetStageCatalogAsync(ct));

    /// <summary>أنواع العقود الفعّالة لنموذج العقد (الرمز والاسم والمسار والفوترة).</summary>
    [HttpGet("types"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> GetTypes([FromServices] IContractTypeService types, CancellationToken ct)
        => Success(await types.ListAsync(activeOnly: true, ct));

    /// <summary>التعديل والحذف في المسودة فقط (ما تسمح به الحالة) مع الصلاحية والنطاق.</summary>
    public override async Task<IActionResult> Update(Guid id, [FromBody] UpdateCommercialContractDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return await base.Update(id, dto, ct);
    }

    public override async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Delete, Record, id, ct: ct);
        return await base.Delete(id, ct);
    }

    /// <summary>حالة العقد والإجراءات المسموحة للمستخدم الحالي (مع ما ينقص كلاً منها)، المراجعات، سجل الانتقالات، المرفقات.</summary>
    [HttpGet("{id:guid}/workflow"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Workflow(Guid id, CancellationToken ct)
        => Success(await _workflow.GetAsync(id, ct));

    /// <summary>دالة الانتقال الوحيدة: الصلاحية والشروط والسبب يفحصها الخادم.</summary>
    [HttpPost("{id:guid}/move"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveContractRequestDto dto, CancellationToken ct)
    {
        var result = await _workflow.MoveAsync(id, dto, ct);
        return Success(result, string.Format(Messages.ContractMoved, result.StatusNameAr));
    }

    [HttpPost("{id:guid}/review"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Review(Guid id, [FromBody] ContractReviewRequestDto dto, CancellationToken ct)
        => Success(await _workflow.ReviewAsync(id, dto, ct), Messages.ContractReviewRecorded);

    [HttpPut("{id:guid}/signing"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Signing(Guid id, [FromBody] ContractSigningDto dto, CancellationToken ct)
        => Success(await _workflow.SaveSigningAsync(id, dto, ct), Messages.ContractSigningSaved);

    [HttpPost("{id:guid}/attachments"), RequireScreen(Screen, ScreenAction.View), RequestSizeLimit(LeaveAttachmentUpload.MaxRequestBytes)]
    public async Task<IActionResult> UploadAttachment(Guid id, [FromQuery] string kind, IFormFile? file, CancellationToken ct)
    {
        var (name, content) = await LeaveAttachmentUpload.ReadAsync(file, ct);
        return Success(await _workflow.AddAttachmentAsync(id, kind, name, content, ct), Messages.CreatedSuccessfully);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var file = await _workflow.GetAttachmentAsync(id, attachmentId, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        await _workflow.DeleteAttachmentAsync(id, attachmentId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    /// <summary>توافق مع الواجهة الحالية حتى تحديثها: الانتقال إلى المرحلة المطلوبة عبر دالة الانتقال نفسها.</summary>
    [HttpPost("{id:guid}/AdvanceStage"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Advance(Guid id, [FromBody] AdvanceStageRequestDto dto, CancellationToken ct)
    {
        await _workflow.MoveAsync(id, new MoveContractRequestDto { ToStatus = dto.Stage, Note = dto.Notes }, ct);
        return Success(await _contracts.GetAsync(id, ct), Messages.ContractAdvanced);
    }

    /// <summary>توافق: الإعادة للمسودة عبر دالة الانتقال (من المراجعة أو الاعتماد، مع السبب الإلزامي).</summary>
    [HttpPost("{id:guid}/reject"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectProcurementRequestDto dto, CancellationToken ct)
    {
        await _workflow.MoveAsync(id, new MoveContractRequestDto { ToStatus = ERP.Core.Models.Shared.ContractStageDefinition.Draft, Note = dto.Reason }, ct);
        return Success(await _contracts.GetAsync(id, ct), Messages.ContractRejectedToDraft);
    }

    // ---------- مستخلصات جدول الكميات ----------
    // الإعداد (إنشاء/تعديل/تقديم/إلغاء) بصلاحية «الاستلام» = استلام الأعمال المنفذة، والاعتماد والإعادة بصلاحية «الاعتماد»؛
    // كلاهما على سجل العقد (النطاق + ما تسمح به حالة العقد: ساري أو استلام أولي).

    [HttpGet("{id:guid}/boq-progress"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> BoqProgress(Guid id, CancellationToken ct)
        => Success(await _certificates.ProgressAsync(id, ct));

    [HttpGet("{id:guid}/certificates"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Certificates(Guid id, CancellationToken ct)
        => Success(await _certificates.ListAsync(id, ct));

    [HttpGet("{id:guid}/certificates/template"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CertificateTemplate(Guid id, CancellationToken ct)
        => Success(await _certificates.TemplateAsync(id, ct));

    [HttpGet("{id:guid}/certificates/{certificateId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Certificate(Guid id, Guid certificateId, CancellationToken ct)
        => Success(await _certificates.GetAsync(id, certificateId, ct));

    [HttpPost("{id:guid}/certificates"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CreateCertificate(Guid id, [FromBody] SaveContractCertificateDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _certificates.CreateAsync(id, dto, ct), Messages.CreatedSuccessfully);
    }

    [HttpPut("{id:guid}/certificates/{certificateId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> UpdateCertificate(Guid id, Guid certificateId, [FromBody] SaveContractCertificateDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _certificates.UpdateAsync(id, certificateId, dto, ct), Messages.UpdatedSuccessfully);
    }

    [HttpPost("{id:guid}/certificates/{certificateId:guid}/submit"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> SubmitCertificate(Guid id, Guid certificateId, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _certificates.SubmitAsync(id, certificateId, ct), Messages.CertificateSubmitted);
    }

    [HttpPost("{id:guid}/certificates/{certificateId:guid}/approve"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ApproveCertificate(Guid id, Guid certificateId, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        return Success(await _certificates.ApproveAsync(id, certificateId, ct), Messages.CertificateApproved);
    }

    [HttpPost("{id:guid}/certificates/{certificateId:guid}/return"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ReturnCertificate(Guid id, Guid certificateId, [FromBody] ContractCertificateActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        return Success(await _certificates.ReturnAsync(id, certificateId, dto, ct), Messages.CertificateReturned);
    }

    [HttpPost("{id:guid}/certificates/{certificateId:guid}/cancel"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CancelCertificate(Guid id, Guid certificateId, [FromBody] ContractCertificateActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _certificates.CancelAsync(id, certificateId, dto, ct), Messages.CertificateCancelled);
    }

    // ---------- فوترة عقود جدول الكميات (صلاحية الترحيل كفوترة الدفعات) ----------

    [HttpPost("{id:guid}/advance-invoice"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> BillAdvance(Guid id, [FromQuery] DateTime? date, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _billing.BillAdvanceAsync(id, date, ct), Messages.ContractAdvanceBilled);
    }

    [HttpPost("{id:guid}/certificates/{certificateId:guid}/bill"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> BillCertificate(Guid id, Guid certificateId, [FromQuery] DateTime? date, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _billing.BillCertificateAsync(id, certificateId, date, ct), Messages.CertificateBilled);
    }

    /// <summary>العقد المبسط: تقديم المستخلص واعتماده وفوترته في خطوة واحدة (صلاحيتا الاعتماد والترحيل معاً).</summary>
    [HttpPost("{id:guid}/certificates/{certificateId:guid}/issue"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> IssueCertificate(Guid id, Guid certificateId, [FromQuery] DateTime? date, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _billing.IssueCertificateAsync(id, certificateId, date, ct), Messages.CertificateIssued);
    }

    [HttpPost("{id:guid}/retention-release"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> ReleaseRetention(Guid id, [FromQuery] DateTime? date, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _billing.ReleaseRetentionAsync(id, date, ct), Messages.RetentionReleased);
    }

    /// <summary>تحصيل (عقد عميل) أو سداد (عقد مورد) فاتورة مستخلص أو دفعة مقدمة بسند موزَّع عليها.</summary>
    [HttpPost("{id:guid}/payments"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> RecordPayment(Guid id, [FromBody] ContractPaymentRequestDto request, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _billing.RecordPaymentAsync(id, request, ct), Messages.ContractPaymentRecorded);
    }

    [HttpPost("{id:guid}/milestones/{milestoneId:guid}/bill"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> Bill(Guid id, Guid milestoneId, [FromQuery] DateTime? date, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _contracts.BillMilestoneAsync(id, milestoneId, date, ct), Messages.MilestoneBilled);
    }
}
