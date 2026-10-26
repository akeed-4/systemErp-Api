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
public class CommercialContractsController : CrudController<CommercialContractDto, CreateCommercialContractDto, UpdateCommercialContractDto>
{
    private const string Screen = PermissionCatalog.CommercialContractsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;
    private readonly ICommercialContractService _contracts;
    private readonly IContractWorkflowService _workflow;
    private readonly IPermissionService _permissions;

    public CommercialContractsController(ICommercialContractService s, IContractWorkflowService workflow, IPermissionService permissions) : base(s)
    {
        _contracts = s; _workflow = workflow; _permissions = permissions;
    }

    [HttpGet("stages")]
    public async Task<IActionResult> GetStages(CancellationToken ct)
        => Success(await _contracts.GetStageCatalogAsync(ct));

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

    [HttpPost("{id:guid}/milestones/{milestoneId:guid}/bill"), RequireScreen(Screen, ScreenAction.Post, Legacy = ScreenAction.Create)]
    public async Task<IActionResult> Bill(Guid id, Guid milestoneId, [FromQuery] DateTime? date, CancellationToken ct)
    {
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Post, Record, id, ScreenAction.Create, ct);
        return Success(await _contracts.BillMilestoneAsync(id, milestoneId, date, ct), Messages.MilestoneBilled);
    }
}
