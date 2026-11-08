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
    private readonly IContractVariationService _variations;

    public CommercialContractsController(ICommercialContractService s, IContractWorkflowService workflow, IContractCertificateService certificates,
        IContractBillingService billing, IPermissionService permissions, IProjectMaterialService materials, IContractVariationService variations) : base(s)
    {
        _contracts = s; _workflow = workflow; _certificates = certificates; _billing = billing; _permissions = permissions; _materials = materials; _variations = variations;
    }

    // ---------- احتياجات مواد المشروع (العقد هو المشروع): الاستلام للإعداد والتقديم (كالمستخلصات، في مراحل التنفيذ)، والاعتماد للاعتماد والإعادة ----------

    private const string MaterialsApproval = "contract-material-requirements";

    [HttpGet("{id:guid}/material-requirements"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> MaterialRequirements(Guid id, CancellationToken ct)
        => Success(await _materials.GetAsync(id, ct));

    /// <summary>معاينة الاحتياجات من جدول الكميات وتحليل بنود المكتبة (لا تُحفظ).</summary>
    [HttpPost("{id:guid}/material-requirements/derive"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DeriveMaterialRequirements(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _materials.DeriveAsync(id, ct));
    }

    /// <summary>مواد المكتبة لسطر في جدول الكميات (تحليل البند بكمية مقترحة، أو المكتبة عامة) للإضافة اليدوية.</summary>
    [HttpGet("{id:guid}/material-requirements/candidates"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> MaterialRequirementCandidates(Guid id, [FromQuery] Guid lineId, [FromQuery] bool all, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _materials.CandidatesAsync(id, lineId, all, ct));
    }

    [HttpPut("{id:guid}/material-requirements"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> SaveMaterialRequirements(Guid id, [FromBody] SaveProjectMaterialPlanDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _materials.SaveAsync(id, dto, ct), Messages.ProjectMaterialPlanSaved);
    }

    [HttpPost("{id:guid}/material-requirements/submit"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> SubmitMaterialRequirements(Guid id, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
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

    // ---------- المناقصة (مسودة عقد المقاولات قبل الترسية) ----------
    // نقل الحالة بصلاحية التعديل على سجل العقد (المسودة تسمح بها)؛ الخادم يحصرها في المسودة.

    [HttpGet("{id:guid}/tender"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Tender(Guid id, [FromServices] IContractTenderService tenders, CancellationToken ct)
        => Success(await tenders.GetAsync(id, ct));

    [HttpGet("{id:guid}/tender/revisions"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> TenderRevisions(Guid id, [FromServices] IContractTenderService tenders, CancellationToken ct)
        => Success(await tenders.RevisionsAsync(id, ct));

    [HttpPost("{id:guid}/tender/move"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> MoveTender(Guid id, [FromBody] MoveTenderDto dto, [FromServices] IContractTenderService tenders, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Edit, Record, id, ct: ct);
        return Success(await tenders.MoveAsync(id, dto, ct), "تم تحديث حالة المناقصة.");
    }

    // ---------- أوامر التغيير (عقود جدول الكميات السارية) ----------
    // الإعداد والتقديم والإلغاء بصلاحية «الاستلام» كالمستخلصات، والاعتماد والإعادة والرفض بصلاحية «الاعتماد»، على سجل العقد.
    // الاعتماد وحده يغيّر العقد ويمر بسياسة الاعتماد إن وُجدت.

    private const string VariationsApproval = "contract-variations";

    [HttpGet("{id:guid}/variations"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Variations(Guid id, CancellationToken ct)
        => Success(await _variations.SummaryAsync(id, ct));

    [HttpGet("{id:guid}/variations/{variationId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Variation(Guid id, Guid variationId, CancellationToken ct)
        => Success(await _variations.GetAsync(id, variationId, ct));

    [HttpPost("{id:guid}/variations"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CreateVariation(Guid id, [FromBody] SaveContractVariationDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _variations.CreateAsync(id, dto, ct), Messages.CreatedSuccessfully);
    }

    [HttpPut("{id:guid}/variations/{variationId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> UpdateVariation(Guid id, Guid variationId, [FromBody] SaveContractVariationDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _variations.UpdateAsync(id, variationId, dto, ct), Messages.UpdatedSuccessfully);
    }

    [HttpDelete("{id:guid}/variations/{variationId:guid}"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> DeleteVariation(Guid id, Guid variationId, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        await _variations.DeleteAsync(id, variationId, ct);
        return Success(Messages.DeletedSuccessfully);
    }

    [HttpPost("{id:guid}/variations/{variationId:guid}/submit"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> SubmitVariation(Guid id, Guid variationId, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _variations.SubmitAsync(id, variationId, ct), "تم تقديم أمر التغيير للاعتماد.");
    }

    /// <summary>الاعتماد يمر بسياسة الاعتماد إن وُجدت (يُحجز بطلب اعتماد ويُنفَّذ عند الاعتماد النهائي).</summary>
    [HttpPost("{id:guid}/variations/{variationId:guid}/approve"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ApproveVariation(Guid id, Guid variationId, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        if (await _variations.ContractIdOfAsync(variationId, ct) != id) return NotFound();
        if (await HoldForApprovalAsync(VariationsApproval, ApprovalDocumentCatalog.Approve, variationId, null, ct) is { } held) return held;
        return Success(await _variations.ApproveAsync(variationId, ct), "تم اعتماد أمر التغيير وتعديل العقد.");
    }

    [HttpPost("{id:guid}/variations/{variationId:guid}/return"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> ReturnVariation(Guid id, Guid variationId, [FromBody] ContractVariationActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        return Success(await _variations.ReturnAsync(id, variationId, dto, ct), "أُعيد أمر التغيير للمسودة.");
    }

    [HttpPost("{id:guid}/variations/{variationId:guid}/reject"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> RejectVariation(Guid id, Guid variationId, [FromBody] ContractVariationActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Approve, Record, id, ct: ct);
        return Success(await _variations.RejectAsync(id, variationId, dto, ct), "تم رفض أمر التغيير.");
    }

    [HttpPost("{id:guid}/variations/{variationId:guid}/cancel"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CancelVariation(Guid id, Guid variationId, [FromBody] ContractVariationActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, id, ct: ct);
        return Success(await _variations.CancelAsync(id, variationId, dto, ct), "تم إلغاء أمر التغيير.");
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

    /// <summary>أسطر أذونات تسليم المواد المرحّلة المتاحة للاختيار في المستخلص (والمختارة في <paramref name="certificateId"/>).</summary>
    [HttpGet("{id:guid}/certificates/material-options"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> CertificateMaterialOptions(Guid id, [FromQuery] Guid? certificateId, CancellationToken ct)
        => Success(await _certificates.MaterialOptionsAsync(id, certificateId, ct));

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

    /// <summary>
    /// إلغاء فاتورة مستخلص مفوتر بإشعار (مرتجع كامل) مع عكس قيدي الاسترداد وتكلفة المواد: عملية ترحيل عكسية، فصلاحيتها صلاحية
    /// الفوترة نفسها (الترحيل) على سجل العقد في مراحل التنفيذ.
    /// </summary>
    [HttpPost("{id:guid}/certificates/{certificateId:guid}/cancel-invoice"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> CancelCertificateInvoice(Guid id, Guid certificateId, [FromBody] ContractCertificateActionDto dto, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _billing.CancelCertificateInvoiceAsync(id, certificateId, dto, ct), "تم إلغاء فاتورة المستخلص وعكس قيودها.");
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

/// <summary>سجل المناقصات لكل العقود (في نطاق وصول المستخدم إلى العقود): الحالة والموعد والقيم.</summary>
[Route("api/v1/tenders")]
[RequireModule(PlatformModules.Accounting)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class TendersIndexController : ErpControllerBase
{
    private readonly IContractTenderService _tenders;

    public TendersIndexController(IContractTenderService tenders) => _tenders = tenders;

    [HttpGet, RequireScreen(PermissionCatalog.CommercialContractsScreenId, ScreenAction.View)]
    public async Task<IActionResult> List(CancellationToken ct) => Success(await _tenders.ListAsync(ct));
}
