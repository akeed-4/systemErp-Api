using ERP.Api.Controllers.Accounting;
using ERP.Api.Infrastructure;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// المرفقات المشتركة لمستندات الوحدات (الورش والتأجير): مشترك بلا وحدة ثابتة لأن الوحدة وشاشة الصلاحية من معالج نوع المالك، والخدمة
/// تفرضهما مع نطاق المستند لكل عملية (بما فيها التنزيل والحذف بمعرّف المرفق).
/// </summary>
[Route("api/v1/attachments")]
public class AttachmentsController : ErpControllerBase
{
    /// <summary>حد طلب الرفع: سقف حجم الملف مع هامش لترويسات multipart.</summary>
    private const long MaxRequestBytes = DocumentAttachmentService.MaxUploadBytes + 64 * 1024;

    private readonly IDocumentAttachmentService _attachments;
    public AttachmentsController(IDocumentAttachmentService attachments) => _attachments = attachments;

    /// <summary>عدد مرفقات المستند وما يملكه المستخدم من إضافة وحذف وحدود الرفع والتصنيفات.</summary>
    [HttpGet("{ownerType}/{ownerId:guid}/summary")]
    public async Task<IActionResult> Summary(string ownerType, Guid ownerId, CancellationToken ct)
        => Success(await _attachments.SummaryAsync(ownerType, ownerId, ct));

    [HttpGet("{ownerType}/{ownerId:guid}")]
    public async Task<IActionResult> List(string ownerType, Guid ownerId, CancellationToken ct)
        => Success(await _attachments.ListAsync(ownerType, ownerId, ct));

    [HttpPost("{ownerType}/{ownerId:guid}"), RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Upload(string ownerType, Guid ownerId, [FromForm] string? category, [FromForm] string? note,
        [FromForm] DateTime? capturedAt, IFormFile? file, CancellationToken ct)
    {
        var (name, content) = await LeaveAttachmentUpload.ReadAsync(file, DocumentAttachmentService.MaxUploadBytes, ct);
        var request = new AddDocumentAttachmentRequest { Category = category, Note = note, CapturedAt = capturedAt };
        return Success(await _attachments.AddAsync(ownerType, ownerId, request, name, content, ct), Messages.CreatedSuccessfully);
    }

    [HttpGet("file/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var file = await _attachments.GetFileAsync(id, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _attachments.DeleteAsync(id, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}
