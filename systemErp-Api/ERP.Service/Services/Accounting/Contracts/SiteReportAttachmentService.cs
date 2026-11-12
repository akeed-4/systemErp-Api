using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.SiteDailyReportStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أرشفة المستندات المؤيدة لكل بند في التقرير اليومي، بآلية أرشيف بنود المستخلص نفسها (المحتوى في قاعدة البيانات، والنوع من توقيع الملف
/// عبر <see cref="LeaveAttachmentService.ContentTypeOf"/>، وتصنيفات المستخلص) وبحد حجم خاص <see cref="MaxBytes"/>. لا تقرأ ولا تغيّر
/// حالة التقرير أو كمياته أو تكلفته. الصلاحيات على شاشة التقرير اليومي وسجل العقد: العرض بالنطاق، والإضافة لمن يُعدّ التقارير
/// (الإضافة أو التعديل) في أي حالة، والحذف في المسودة لمن يُعدّها وبعدها (مقدَّم/معتمد) بصلاحية الحذف.
/// </summary>
public class SiteReportAttachmentService : ISiteReportAttachmentService
{
    /// <summary>حد حجم الملف الواحد في أرشيف التقرير اليومي (2 ميغابايت).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxPerItem = 20;
    private const int MaxFileNameLength = 150;
    private const int MaxNoteLength = 300;
    private const string Screen = PermissionCatalog.SiteDailyReportsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public SiteReportAttachmentService(ErpDbContext db, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _user = user; _permissions = permissions; _audit = audit;
    }

    public async Task<SiteReportAttachmentSummaryDto> SummaryAsync(Guid contractId, Guid reportId, CancellationToken ct = default)
    {
        var report = await LoadAsync(contractId, reportId, ct);
        var counts = await _db.Set<SiteReportItemAttachment>().AsNoTracking()
            .Where(a => a.ReportId == report.Id)
            .GroupBy(a => a.ContractItemId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new SiteReportAttachmentSummaryDto
        {
            Counts = counts,
            CanAdd = await CanPrepareAsync(ct),
            CanDelete = await CanDeleteAsync(report, ct),
            MaxPerItem = MaxPerItem,
            MaxBytes = MaxBytes,
        };
    }

    public async Task<List<SiteReportItemAttachmentDto>> ListAsync(Guid contractId, Guid reportId, Guid? contractItemId, CancellationToken ct = default)
    {
        var report = await LoadAsync(contractId, reportId, ct);
        var canDelete = await CanDeleteAsync(report, ct);
        return await _db.Set<SiteReportItemAttachment>().AsNoTracking()
            .Where(a => a.ReportId == report.Id && (contractItemId == null || a.ContractItemId == contractItemId))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new SiteReportItemAttachmentDto
            {
                Id = a.Id, ReportId = a.ReportId, ContractItemId = a.ContractItemId, Category = a.Category, Note = a.Note,
                FileName = a.FileName, ContentType = a.ContentType, Size = a.Size, UploadedBy = a.UploadedBy, CreatedAt = a.CreatedAt,
                CanDelete = canDelete,
            })
            .ToListAsync(ct);
    }

    public async Task<SiteReportItemAttachmentDto> AddAsync(Guid contractId, Guid reportId, Guid contractItemId, string? category, string? note,
        string fileName, byte[] content, CancellationToken ct = default)
    {
        var report = await LoadAsync(contractId, reportId, ct);
        await AuthorizePrepareAsync(ct);

        if (!await _db.Set<CommercialContractItem>().AnyAsync(i => i.Id == contractItemId && i.ContractId == contractId && !i.IsGroup, ct))
            throw new ValidationFailedException(Messages.SiteReportAttachmentUnknownItem);
        category = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (category.Length == 0) category = CertificateAttachmentCategories.Other;
        if (!CertificateAttachmentCategories.All.Contains(category)) throw new ValidationFailedException(Messages.CertificateAttachmentCategoryInvalid);

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0 || content.Length == 0) throw new ValidationFailedException(Messages.LeaveAttachmentEmpty);
        if (content.Length > MaxBytes) throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentTooLarge, MaxBytes / (1024 * 1024)));
        var contentType = LeaveAttachmentService.ContentTypeOf(name, content) ?? throw new ValidationFailedException(Messages.LeaveAttachmentTypeNotAllowed);
        if (await _db.Set<SiteReportItemAttachment>().CountAsync(a => a.ReportId == report.Id && a.ContractItemId == contractItemId, ct) >= MaxPerItem)
            throw new ValidationFailedException(string.Format(Messages.SiteReportAttachmentLimitReached, MaxPerItem));

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > MaxNoteLength }) note = note[..MaxNoteLength];
        var attachment = new SiteReportItemAttachment
        {
            ContractId = contractId, ReportId = report.Id, ContractItemId = contractItemId, Category = category, Note = note,
            FileName = name.Length > MaxFileNameLength ? name[^MaxFileNameLength..] : name,
            ContentType = contentType, Size = content.Length, Content = content,
            UploadedBy = _user.Name ?? _user.Email, UploadedByUserId = _user.UserId,
        };
        _db.Add(attachment);
        await _db.SaveChangesAsync(ct);
        await LogAsync(Events.Added, report, contractItemId, attachment.FileName, ct);
        return new SiteReportItemAttachmentDto
        {
            Id = attachment.Id, ReportId = report.Id, ContractItemId = contractItemId, Category = category, Note = note,
            FileName = attachment.FileName, ContentType = contentType, Size = attachment.Size, UploadedBy = attachment.UploadedBy,
            CreatedAt = attachment.CreatedAt, CanDelete = await CanDeleteAsync(report, ct),
        };
    }

    public async Task<ContractFileDto> GetFileAsync(Guid contractId, Guid reportId, Guid attachmentId, CancellationToken ct = default)
    {
        var report = await LoadAsync(contractId, reportId, ct);
        var a = await _db.Set<SiteReportItemAttachment>().AsNoTracking()
                .Where(x => x.Id == attachmentId && x.ReportId == report.Id)
                .Select(x => new { x.FileName, x.ContentType, x.Content })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.ContractAttachmentNotFound);
        return new ContractFileDto(a.FileName, a.ContentType, a.Content);
    }

    public async Task DeleteAsync(Guid contractId, Guid reportId, Guid attachmentId, CancellationToken ct = default)
    {
        var report = await LoadAsync(contractId, reportId, ct);
        if (report.Status == St.Draft) await AuthorizePrepareAsync(ct);
        else if (!await _permissions.HasPermissionAsync(Screen, ScreenAction.Delete, ct))
            throw new ForbiddenException(Messages.SiteReportAttachmentDeleteForbidden);

        // يُحذف بالمعرّف دون تحميل المحتوى
        var a = await _db.Set<SiteReportItemAttachment>().AsNoTracking()
                .Where(x => x.Id == attachmentId && x.ReportId == report.Id)
                .Select(x => new { x.Id, x.TenantId, x.ContractItemId, x.FileName })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.ContractAttachmentNotFound);
        _db.Remove(new SiteReportItemAttachment { Id = a.Id, TenantId = a.TenantId });
        await _db.SaveChangesAsync(ct);
        await LogAsync(Events.Deleted, report, a.ContractItemId, a.FileName, ct);
    }

    /// <summary>
    /// مستندات التقرير تُحذف معه حين يُحذف (مسودة التقرير أو العقد بكامله)؛ بلا تحميل المحتوى. لا يحفظ (يحفظه المستدعي في معاملته).
    /// </summary>
    internal static async Task RemoveForReportsAsync(ErpDbContext db, IQueryable<Guid> reportIds, CancellationToken ct)
    {
        var rows = await db.Set<SiteReportItemAttachment>().AsNoTracking().Where(a => reportIds.Contains(a.ReportId))
            .Select(a => new { a.Id, a.TenantId }).ToListAsync(ct);
        foreach (var r in rows) db.Remove(new SiteReportItemAttachment { Id = r.Id, TenantId = r.TenantId });
    }

    // ---------- مساعدات ----------

    private static class Events
    {
        public const string Added = "SiteReportAttachmentAdded";
        public const string Deleted = "SiteReportAttachmentDeleted";
    }

    private sealed record ReportRef(Guid Id, Guid ContractId, DateTime ReportDate, string Status);

    /// <summary>التقرير على العقد ضمن نطاق المستخدم.</summary>
    private async Task<ReportRef> LoadAsync(Guid contractId, Guid reportId, CancellationToken ct)
    {
        await _permissions.EnsureRecordInScopeAsync(Record, contractId, ct);
        return await _db.Set<SiteDailyReport>().AsNoTracking()
                .Where(r => r.Id == reportId && r.ContractId == contractId)
                .Select(r => new ReportRef(r.Id, r.ContractId, r.ReportDate, r.Status))
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.SiteReportNotFound);
    }

    /// <summary>
    /// إعداد التقارير = صلاحية الإضافة أو التعديل على شاشة التقرير اليومي، والعقد في نطاق المستخدم (فُحص في <see cref="LoadAsync"/>)،
    /// كما تُفحص عمليات التقرير نفسها. مصفوفة مراحل العقد لا تسري هنا (التقرير اليومي ليس إجراءً على مرحلة العقد).
    /// </summary>
    private async Task AuthorizePrepareAsync(CancellationToken ct)
    {
        if (!await CanPrepareAsync(ct)) throw new ForbiddenException(Messages.SiteReportAttachmentAddForbidden);
    }

    private async Task<bool> CanPrepareAsync(CancellationToken ct)
        => await _permissions.HasPermissionAsync(Screen, ScreenAction.Create, ct) || await _permissions.HasPermissionAsync(Screen, ScreenAction.Edit, ct);

    private async Task<bool> CanDeleteAsync(ReportRef report, CancellationToken ct) => report.Status == St.Draft
        ? await CanPrepareAsync(ct)
        : await _permissions.HasPermissionAsync(Screen, ScreenAction.Delete, ct);

    private Task LogAsync(string action, ReportRef report, Guid contractItemId, string fileName, CancellationToken ct)
        => _audit.LogAsync(action, nameof(SiteDailyReport), report.Id.ToString(), $"{report.ReportDate:yyyy-MM-dd} / {contractItemId}: {fileName}", ct);
}
