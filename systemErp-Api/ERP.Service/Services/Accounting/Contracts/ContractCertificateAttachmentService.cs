using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.ContractCertificateStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أرشفة المستندات المؤيدة لكل بند في المستخلص، بآلية مرفقات النظام نفسها (المحتوى في قاعدة البيانات، والنوع من توقيع الملف
/// عبر <see cref="LeaveAttachmentService.ContentTypeOf"/>، وحد الحجم نفسه). لا تقرأ ولا تغيّر حالة المستخلص أو قيمه أو قيوده.
/// الصلاحيات على سجل العقد: العرض بالنطاق، والإضافة بصلاحية إعداد المستخلص (الاستلام) ما لم يُلغَ، والحذف في المسودة بصلاحية
/// الإعداد وبعدها (مقدَّم/معتمد/مفوتر) بصلاحية الحذف على شاشة العقود؛ المستخلص الملغى يبقى أرشيفه للقراءة فقط.
/// </summary>
public class ContractCertificateAttachmentService : IContractCertificateAttachmentService
{
    public const int MaxPerItem = 20;
    private const int MaxFileNameLength = 150;
    private const int MaxNoteLength = 300;
    private const string Screen = PermissionCatalog.CommercialContractsScreenId;
    private const string Record = ScopedRecordTypes.CommercialContractKey;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public ContractCertificateAttachmentService(ErpDbContext db, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _user = user; _permissions = permissions; _audit = audit;
    }

    public async Task<CertificateAttachmentSummaryDto> SummaryAsync(Guid contractId, Guid certificateId, CancellationToken ct = default)
    {
        var cert = await LoadAsync(contractId, certificateId, ct);
        var counts = await _db.Set<ContractCertificateItemAttachment>().AsNoTracking()
            .Where(a => a.CertificateId == cert.Id)
            .GroupBy(a => a.ContractItemId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new CertificateAttachmentSummaryDto
        {
            Counts = counts,
            CanAdd = await CanAddAsync(cert, ct),
            CanDelete = await CanDeleteAsync(cert, ct),
            MaxPerItem = MaxPerItem,
            MaxBytes = LeaveAttachmentService.MaxBytes,
        };
    }

    public async Task<List<CertificateItemAttachmentDto>> ListAsync(Guid contractId, Guid certificateId, Guid? contractItemId, CancellationToken ct = default)
    {
        var cert = await LoadAsync(contractId, certificateId, ct);
        var canDelete = await CanDeleteAsync(cert, ct);
        return await _db.Set<ContractCertificateItemAttachment>().AsNoTracking()
            .Where(a => a.CertificateId == cert.Id && (contractItemId == null || a.ContractItemId == contractItemId))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new CertificateItemAttachmentDto
            {
                Id = a.Id, CertificateId = a.CertificateId, ContractItemId = a.ContractItemId, Category = a.Category, Note = a.Note,
                FileName = a.FileName, ContentType = a.ContentType, Size = a.Size, UploadedBy = a.UploadedBy, CreatedAt = a.CreatedAt,
                CanDelete = canDelete,
            })
            .ToListAsync(ct);
    }

    public async Task<CertificateItemAttachmentDto> AddAsync(Guid contractId, Guid certificateId, Guid contractItemId, string? category, string? note,
        string fileName, byte[] content, CancellationToken ct = default)
    {
        var cert = await LoadAsync(contractId, certificateId, ct);
        if (cert.Status == St.Cancelled) throw new ConflictException(Messages.CertificateAttachmentCancelled);
        await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, contractId, ct: ct);

        if (!await _db.Set<CommercialContractItem>().AnyAsync(i => i.Id == contractItemId && i.ContractId == contractId, ct))
            throw new ValidationFailedException(Messages.CertificateLineUnknownItem);
        category = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (category.Length == 0) category = CertificateAttachmentCategories.Other;
        if (!CertificateAttachmentCategories.All.Contains(category)) throw new ValidationFailedException(Messages.CertificateAttachmentCategoryInvalid);

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0 || content.Length == 0) throw new ValidationFailedException(Messages.LeaveAttachmentEmpty);
        if (content.Length > LeaveAttachmentService.MaxBytes)
            throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentTooLarge, LeaveAttachmentService.MaxBytes / (1024 * 1024)));
        var contentType = LeaveAttachmentService.ContentTypeOf(name, content) ?? throw new ValidationFailedException(Messages.LeaveAttachmentTypeNotAllowed);
        if (await _db.Set<ContractCertificateItemAttachment>().CountAsync(a => a.CertificateId == cert.Id && a.ContractItemId == contractItemId, ct) >= MaxPerItem)
            throw new ValidationFailedException(string.Format(Messages.CertificateAttachmentLimitReached, MaxPerItem));

        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > MaxNoteLength }) note = note[..MaxNoteLength];
        var attachment = new ContractCertificateItemAttachment
        {
            ContractId = contractId, ProjectContractId = cert.ProjectContractId, CertificateId = cert.Id, ContractItemId = contractItemId,
            Category = category, Note = note,
            FileName = name.Length > MaxFileNameLength ? name[^MaxFileNameLength..] : name,
            ContentType = contentType, Size = content.Length, Content = content,
            UploadedBy = _user.Name ?? _user.Email, UploadedByUserId = _user.UserId,
        };
        _db.Add(attachment);
        await _db.SaveChangesAsync(ct);
        await LogAsync(Events.Added, cert, attachment.ContractItemId, attachment.FileName, ct);
        return new CertificateItemAttachmentDto
        {
            Id = attachment.Id, CertificateId = cert.Id, ContractItemId = contractItemId, Category = category, Note = note,
            FileName = attachment.FileName, ContentType = contentType, Size = attachment.Size, UploadedBy = attachment.UploadedBy,
            CreatedAt = attachment.CreatedAt, CanDelete = await CanDeleteAsync(cert, ct),
        };
    }

    public async Task<ContractFileDto> GetFileAsync(Guid contractId, Guid certificateId, Guid attachmentId, CancellationToken ct = default)
    {
        var cert = await LoadAsync(contractId, certificateId, ct);
        var a = await _db.Set<ContractCertificateItemAttachment>().AsNoTracking()
                .Where(x => x.Id == attachmentId && x.CertificateId == cert.Id)
                .Select(x => new { x.FileName, x.ContentType, x.Content })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.ContractAttachmentNotFound);
        return new ContractFileDto(a.FileName, a.ContentType, a.Content);
    }

    public async Task DeleteAsync(Guid contractId, Guid certificateId, Guid attachmentId, CancellationToken ct = default)
    {
        var cert = await LoadAsync(contractId, certificateId, ct);
        if (cert.Status == St.Cancelled) throw new ConflictException(Messages.CertificateAttachmentCancelled);
        if (cert.Status == St.Draft) await _permissions.AuthorizeRecordAsync(Screen, ScreenAction.Receive, Record, contractId, ct: ct);
        else if (!await _permissions.HasPermissionAsync(Screen, ScreenAction.Delete, ct))
            throw new ForbiddenException(Messages.CertificateAttachmentDeleteForbidden);

        // يُحذف بالمعرّف دون تحميل المحتوى
        var a = await _db.Set<ContractCertificateItemAttachment>().AsNoTracking()
                .Where(x => x.Id == attachmentId && x.CertificateId == cert.Id)
                .Select(x => new { x.Id, x.TenantId, x.ContractItemId, x.FileName })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.ContractAttachmentNotFound);
        _db.Remove(new ContractCertificateItemAttachment { Id = a.Id, TenantId = a.TenantId });
        await _db.SaveChangesAsync(ct);
        await LogAsync(Events.Deleted, cert, a.ContractItemId, a.FileName, ct);
    }

    // ---------- مساعدات ----------

    private static class Events
    {
        public const string Added = "CertificateAttachmentAdded";
        public const string Deleted = "CertificateAttachmentDeleted";
    }

    private sealed record CertificateRef(Guid Id, Guid ContractId, string CertificateNumber, string Status, Guid ProjectContractId);

    /// <summary>المستخلص على العقد ضمن نطاق المستخدم، مع عقد المشروع (الأب لعقد الباطن).</summary>
    private async Task<CertificateRef> LoadAsync(Guid contractId, Guid certificateId, CancellationToken ct)
    {
        await _permissions.EnsureRecordInScopeAsync(Record, contractId, ct);
        var cert = await _db.Set<ContractCertificate>().AsNoTracking()
                .Where(c => c.Id == certificateId && c.ContractId == contractId)
                .Select(c => new { c.Id, c.CertificateNumber, c.Status })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.CertificateNotFound);
        var parentId = await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == contractId)
            .Select(c => c.ParentContractId).FirstOrDefaultAsync(ct);
        return new CertificateRef(cert.Id, contractId, cert.CertificateNumber, cert.Status, parentId ?? contractId);
    }

    private async Task<bool> CanAddAsync(CertificateRef cert, CancellationToken ct)
        => cert.Status != St.Cancelled && await AllowedAsync(ScreenAction.Receive, cert.ContractId, ct);

    private async Task<bool> CanDeleteAsync(CertificateRef cert, CancellationToken ct) => cert.Status switch
    {
        St.Cancelled => false,
        St.Draft => await AllowedAsync(ScreenAction.Receive, cert.ContractId, ct),
        _ => await _permissions.HasPermissionAsync(Screen, ScreenAction.Delete, ct),
    };

    /// <summary>فحص صلاحية السجل نفسه الذي تفرضه العملية، كقيمة منطقية لعرض الأزرار.</summary>
    private async Task<bool> AllowedAsync(ScreenAction action, Guid contractId, CancellationToken ct)
    {
        try
        {
            await _permissions.AuthorizeRecordAsync(Screen, action, Record, contractId, ct: ct);
            return true;
        }
        catch (ForbiddenException)
        {
            return false;
        }
    }

    private Task LogAsync(string action, CertificateRef cert, Guid contractItemId, string fileName, CancellationToken ct)
        => _audit.LogAsync(action, nameof(ContractCertificate), cert.Id.ToString(), $"{cert.CertificateNumber} / {contractItemId}: {fileName}", ct);
}
