using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// بوابة الموظف: طلبات إجازته ومرفقاتها. لا تتطلب صلاحية شاشة؛ الموظف يُحدَّد من جلسة الدخول، فكل مسار هنا
/// يعمل على طلبات صاحب الجلسة فقط.
/// </summary>
[Route("api/v1/me/leaves"), EmployeeSelfService, RequireModule(PlatformModules.Hr)]
public class MyLeavesController : ErpControllerBase
{
    private readonly IEmployeePortalService _portal;
    public MyLeavesController(IEmployeePortalService portal) => _portal = portal;

    /// <summary>بيانات الموظف ورصيده وأنواع الإجازات وطلباته بمرفقاتها.</summary>
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct) => Success(await _portal.GetOverviewAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMyLeaveRequestDto dto, CancellationToken ct)
    {
        var created = await _portal.CreateLeaveAsync(dto, ct);
        return Created($"{Request.Path}/{created.Id}", ApiResponse<LeaveRequestDto>.Ok(created, Messages.CreatedSuccessfully).WithStatus(201));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => Success(await _portal.CancelLeaveAsync(id, ct), Messages.LeaveCancelled);

    [HttpPost("{id:guid}/attachments"), RequestSizeLimit(LeaveAttachmentUpload.MaxRequestBytes)]
    public async Task<IActionResult> Upload(Guid id, IFormFile? file, CancellationToken ct)
    {
        var (name, content) = await LeaveAttachmentUpload.ReadAsync(file, ct);
        return Success(await _portal.AddAttachmentAsync(id, name, content, ct), Messages.CreatedSuccessfully);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var file = await _portal.GetAttachmentAsync(id, attachmentId, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct)
    {
        await _portal.DeleteAttachmentAsync(id, attachmentId, ct);
        return Success(Messages.DeletedSuccessfully);
    }
}

/// <summary>قراءة ملف مرفوع إلى الذاكرة بحدّ حجم المرفق (التحقق من النوع والعدد في الخدمة).</summary>
internal static class LeaveAttachmentUpload
{
    /// <summary>حد الطلب: حجم المرفق مع هامش لترويسات multipart.</summary>
    public const long MaxRequestBytes = ERP.Service.Services.Accounting.LeaveAttachmentService.MaxBytes + 64 * 1024;

    public static async Task<(string Name, byte[] Content)> ReadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) throw new ValidationFailedException(Messages.LeaveAttachmentEmpty);
        var max = ERP.Service.Services.Accounting.LeaveAttachmentService.MaxBytes;
        if (file.Length > max) throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentTooLarge, max / (1024 * 1024)));
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return (file.FileName, buffer.ToArray());
    }
}
