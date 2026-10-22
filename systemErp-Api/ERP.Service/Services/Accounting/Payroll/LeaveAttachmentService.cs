using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مرفقات طلبات الإجازات (تقرير طبي مثلاً): PDF أو صورة، بحجم وعدد محدودين، ويُتحقق من نوع الملف من محتواه
/// لا من اسمه. القوائم لا تحمّل المحتوى؛ يُقرأ عند التنزيل فقط.
/// </summary>
public class LeaveAttachmentService : ILeaveAttachmentService
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerRequest = 5;
    private const int MaxFileNameLength = 150;

    private static readonly (string ContentType, string[] Extensions, byte[] Signature)[] Allowed =
    {
        ("application/pdf", new[] { ".pdf" }, new byte[] { 0x25, 0x50, 0x44, 0x46 }),
        ("image/jpeg", new[] { ".jpg", ".jpeg" }, new byte[] { 0xFF, 0xD8, 0xFF }),
        ("image/png", new[] { ".png" }, new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
    };

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public LeaveAttachmentService(ErpDbContext db, ICurrentUser user, IAuditService audit)
    {
        _db = db; _user = user; _audit = audit;
    }

    public async Task<List<LeaveAttachmentDto>> ListAsync(Guid leaveRequestId, Guid? ownerEmployeeId = null, CancellationToken ct = default)
    {
        await FindLeaveAsync(leaveRequestId, ownerEmployeeId, tracked: false, ct);
        return await Metadata(_db.Set<LeaveAttachment>().AsNoTracking().Where(a => a.LeaveRequestId == leaveRequestId))
            .OrderBy(a => a.CreatedAt).ToListAsync(ct);
    }

    public async Task<LeaveAttachmentDto> AddAsync(Guid leaveRequestId, string fileName, byte[] content, Guid? ownerEmployeeId = null, CancellationToken ct = default)
    {
        var leave = await FindLeaveAsync(leaveRequestId, ownerEmployeeId, tracked: true, ct);
        EnsureOwnerMayChange(leave, ownerEmployeeId);

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0 || content.Length == 0) throw new ValidationFailedException(Messages.LeaveAttachmentEmpty);
        if (content.Length > MaxBytes) throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentTooLarge, MaxBytes / (1024 * 1024)));
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var type = Allowed.FirstOrDefault(t => t.Extensions.Contains(extension) && content.AsSpan().StartsWith(t.Signature));
        if (type.ContentType == null) throw new ValidationFailedException(Messages.LeaveAttachmentTypeNotAllowed);
        if (await _db.Set<LeaveAttachment>().CountAsync(a => a.LeaveRequestId == leaveRequestId, ct) >= MaxPerRequest)
            throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentLimitReached, MaxPerRequest));

        var attachment = new LeaveAttachment
        {
            Id = Guid.NewGuid(), LeaveRequestId = leaveRequestId,
            FileName = name.Length > MaxFileNameLength ? name[^MaxFileNameLength..] : name,
            ContentType = type.ContentType, Size = content.Length, Content = content, UploadedBy = _user.Name ?? _user.Email,
        };
        _db.Add(attachment);
        leave.AttachmentCount++;
        await _db.SaveChangesAsync(ct);
        await LeaveService.LogEventAsync(_audit, leave, LeaveService.Events.AttachmentAdded, attachment.FileName, ct);
        return new LeaveAttachmentDto
        {
            Id = attachment.Id, CreatedAt = attachment.CreatedAt, LeaveRequestId = leaveRequestId, FileName = attachment.FileName,
            ContentType = attachment.ContentType, Size = attachment.Size, UploadedBy = attachment.UploadedBy,
        };
    }

    public async Task<LeaveAttachmentFileDto> GetFileAsync(Guid leaveRequestId, Guid attachmentId, Guid? ownerEmployeeId = null, CancellationToken ct = default)
    {
        var leave = await FindLeaveAsync(leaveRequestId, ownerEmployeeId, tracked: false, ct);
        var file = await _db.Set<LeaveAttachment>().AsNoTracking().Where(a => a.Id == attachmentId && a.LeaveRequestId == leaveRequestId)
                .Select(a => new LeaveAttachmentFileDto { FileName = a.FileName, ContentType = a.ContentType, Content = a.Content })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.LeaveAttachmentNotFound);
        // التقرير الطبي بيانات حساسة: اطلاع غير صاحبه عليه يُسجَّل في سجل التدقيق
        if (ownerEmployeeId == null) await LeaveService.LogEventAsync(_audit, leave, LeaveService.Events.AttachmentViewed, file.FileName, ct);
        return file;
    }

    public async Task DeleteAsync(Guid leaveRequestId, Guid attachmentId, Guid? ownerEmployeeId = null, CancellationToken ct = default)
    {
        var leave = await FindLeaveAsync(leaveRequestId, ownerEmployeeId, tracked: true, ct);
        EnsureOwnerMayChange(leave, ownerEmployeeId);
        // يُحذف بالمعرّف دون تحميل المحتوى
        var name = await _db.Set<LeaveAttachment>().Where(a => a.Id == attachmentId && a.LeaveRequestId == leaveRequestId).Select(a => a.FileName).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.LeaveAttachmentNotFound);
        _db.Remove(new LeaveAttachment { Id = attachmentId, TenantId = leave.TenantId });
        leave.AttachmentCount = Math.Max(0, leave.AttachmentCount - 1);
        await _db.SaveChangesAsync(ct);
        await LeaveService.LogEventAsync(_audit, leave, LeaveService.Events.AttachmentDeleted, name, ct);
    }

    internal static IQueryable<LeaveAttachmentDto> Metadata(IQueryable<LeaveAttachment> attachments)
        => attachments.Select(a => new LeaveAttachmentDto
        {
            Id = a.Id, CreatedAt = a.CreatedAt, LeaveRequestId = a.LeaveRequestId, FileName = a.FileName,
            ContentType = a.ContentType, Size = a.Size, UploadedBy = a.UploadedBy,
        });

    private async Task<LeaveRequest> FindLeaveAsync(Guid id, Guid? ownerEmployeeId, bool tracked, CancellationToken ct)
    {
        var query = tracked ? _db.Set<LeaveRequest>() : _db.Set<LeaveRequest>().AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Id == id && (ownerEmployeeId == null || r.EmployeeId == ownerEmployeeId), ct)
            ?? throw new NotFoundException(Messages.LeaveRequestNotFound);
    }

    /// <summary>الموظف يعدّل مرفقات طلبه ما دام قائماً (معلّقاً أو معاداً للاستكمال)؛ بعد البتّ فيه تبقى كما اعتُمد عليها.</summary>
    private static void EnsureOwnerMayChange(LeaveRequest leave, Guid? ownerEmployeeId)
    {
        if (ownerEmployeeId != null && !LeaveStatuses.Open.Contains(leave.Status)) throw new ConflictException(Messages.LeaveRequestNotPending);
    }
}
