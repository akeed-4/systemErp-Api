using System.Security.Cryptography;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// المرفقات المشتركة: المالك يُحل بمعالجه المسجّل (<see cref="IAttachmentOwner"/>)، والترخيص وصلاحية العرض على شاشته تُفرض هنا لكل
/// عملية (والتنزيل والحذف يحلّان مالك المرفق نفسه فلا يُفتح مرفق مستند خارج نطاق المستخدم). نوع الملف من امتداده وتوقيعه معاً
/// (<see cref="LeaveAttachmentService.ContentTypeOf"/>: PDF/JPEG/PNG)، والحجم والعدد والتصنيفات بحدود المالك. المحتوى عند مزود التخزين
/// الافتراضي (<see cref="DefaultProvider"/>)، والقراءة والحذف من مزود السجل نفسه.
/// </summary>
public class DocumentAttachmentService : IDocumentAttachmentService
{
    /// <summary>مزود تخزين المرفقات الجديدة.</summary>
    public const string DefaultProvider = AttachmentStorageProviders.Database;
    /// <summary>سقف حجم الملف لأي مالك (حد طلب الرفع)؛ حد المالك يُطبَّق داخله.</summary>
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    private const int MaxFileNameLength = 150;
    private const int MaxNoteLength = 300;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;
    private readonly ISubscriptionEntitlements _entitlements;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, IAttachmentOwner> _owners;
    private readonly Dictionary<string, IAttachmentStorage> _storages;

    public DocumentAttachmentService(ErpDbContext db, ICurrentUser user, IPermissionService permissions, IAuditService audit,
        ISubscriptionEntitlements entitlements, TimeProvider clock, IEnumerable<IAttachmentOwner> owners, IEnumerable<IAttachmentStorage> storages)
    {
        _db = db; _user = user; _permissions = permissions; _audit = audit; _entitlements = entitlements; _clock = clock;
        _owners = owners.ToDictionary(o => o.OwnerType, StringComparer.Ordinal);
        _storages = storages.ToDictionary(s => s.Provider, StringComparer.Ordinal);
    }

    public async Task<DocumentAttachmentSummaryDto> SummaryAsync(string ownerType, Guid ownerId, CancellationToken ct = default)
    {
        var (owner, access) = await ResolveAsync(ownerType, ownerId, ct);
        return new DocumentAttachmentSummaryDto
        {
            Count = await Rows(owner.OwnerType, ownerId).CountAsync(ct),
            CanAdd = access.CanAdd,
            CanDelete = access.CanDelete,
            MaxCount = owner.MaxPerOwner,
            MaxBytes = Math.Min(owner.MaxBytes, MaxUploadBytes),
            Categories = owner.Categories.ToArray(),
        };
    }

    public async Task<List<DocumentAttachmentDto>> ListAsync(string ownerType, Guid ownerId, CancellationToken ct = default)
    {
        var (owner, access) = await ResolveAsync(ownerType, ownerId, ct);
        return await Rows(owner.OwnerType, ownerId)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new DocumentAttachmentDto
            {
                Id = a.Id, OwnerType = a.OwnerType, OwnerId = a.OwnerId, Category = a.Category, Note = a.Note, FileName = a.FileName,
                ContentType = a.ContentType, Size = a.Size, CapturedAt = a.CapturedAt, UploadedBy = a.UploadedBy, CreatedAt = a.CreatedAt,
                CanDelete = access.CanDelete,
            })
            .ToListAsync(ct);
    }

    public async Task<DocumentAttachmentDto> AddAsync(string ownerType, Guid ownerId, AddDocumentAttachmentRequest request, string fileName,
        byte[] content, CancellationToken ct = default)
    {
        var (owner, access) = await ResolveAsync(ownerType, ownerId, ct);
        if (!access.CanAdd) throw new ForbiddenException(Messages.AttachmentAddForbidden);

        var category = (request.Category ?? string.Empty).Trim().ToLowerInvariant();
        if (category.Length == 0)
            category = owner.Categories.Contains(DocumentAttachmentCategories.Other) ? DocumentAttachmentCategories.Other : owner.Categories.First();
        if (!owner.Categories.Contains(category)) throw new ValidationFailedException(Messages.AttachmentCategoryInvalid);

        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (name.Length == 0 || content.Length == 0) throw new ValidationFailedException(Messages.LeaveAttachmentEmpty);
        var maxBytes = Math.Min(owner.MaxBytes, MaxUploadBytes);
        if (content.Length > maxBytes)
            throw new ValidationFailedException(string.Format(Messages.LeaveAttachmentTooLarge, Math.Max(1, maxBytes / (1024 * 1024))));
        var contentType = LeaveAttachmentService.ContentTypeOf(name, content) ?? throw new ValidationFailedException(Messages.LeaveAttachmentTypeNotAllowed);
        if (await Rows(owner.OwnerType, ownerId).CountAsync(ct) >= owner.MaxPerOwner)
            throw new ValidationFailedException(string.Format(Messages.AttachmentLimitReached, owner.MaxPerOwner));

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > MaxNoteLength }) note = note[..MaxNoteLength];
        // وقت الالتقاط من الجهاز للعرض فقط؛ المستقبلي (ساعة جهاز خاطئة) يُهمل
        var captured = request.CapturedAt is { } at && at.ToUniversalTime() <= _clock.GetUtcNow().UtcDateTime ? at.ToUniversalTime() : (DateTime?)null;

        // المعرّف قبل الحفظ ليخزّن المزود المحتوى بمرجعه؛ مزود قاعدة البيانات يضيفه للسياق فيُحفظان معاً
        var attachment = new DocumentAttachment
        {
            Id = Guid.NewGuid(), OwnerType = owner.OwnerType, OwnerId = ownerId, Category = category, Note = note,
            FileName = name.Length > MaxFileNameLength ? name[^MaxFileNameLength..] : name,
            ContentType = contentType, Size = content.Length, Checksum = Convert.ToHexString(SHA256.HashData(content)),
            StorageProvider = DefaultProvider, CapturedAt = captured,
            UploadedBy = _user.Name ?? _user.Email, UploadedByUserId = _user.UserId,
        };
        attachment.StorageKey = await Storage(DefaultProvider).StoreAsync(attachment.Id, content, ct);
        _db.Add(attachment);
        await _db.SaveChangesAsync(ct);
        await LogAsync(Events.Added, access, attachment.FileName, ct);
        return new DocumentAttachmentDto
        {
            Id = attachment.Id, OwnerType = attachment.OwnerType, OwnerId = ownerId, Category = category, Note = note, FileName = attachment.FileName,
            ContentType = contentType, Size = attachment.Size, CapturedAt = captured, UploadedBy = attachment.UploadedBy,
            CreatedAt = attachment.CreatedAt, CanDelete = access.CanDelete,
        };
    }

    public async Task<DocumentAttachmentFileDto> GetFileAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var row = await FindAsync(attachmentId, ct);
        await ResolveAsync(row.OwnerType, row.OwnerId, ct);
        var content = await Storage(row.StorageProvider).ReadAsync(row.StorageKey, ct);
        return new DocumentAttachmentFileDto(row.FileName, row.ContentType, content);
    }

    public async Task DeleteAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var row = await FindAsync(attachmentId, ct);
        var (_, access) = await ResolveAsync(row.OwnerType, row.OwnerId, ct);
        if (!access.CanDelete) throw new ForbiddenException(Messages.AttachmentDeleteForbidden);

        await Storage(row.StorageProvider).RemoveAsync(row.StorageKey, ct);
        _db.Remove(new DocumentAttachment { Id = row.Id, TenantId = row.TenantId });
        await _db.SaveChangesAsync(ct);
        await LogAsync(Events.Deleted, access, row.FileName, ct);
    }

    /// <summary>
    /// مرفقات مستندات تُحذف معها (مسودة تُحذف مثلاً)، بلا تحميل المحتوى ولا فحص صلاحية (يفحصها مستدعيه). لا يحفظ: يحفظه المستدعي في
    /// معاملته. لمزود قاعدة البيانات وحده؛ مزود خارجي يُضاف هنا مع أول استعمال له.
    /// </summary>
    internal static async Task RemoveForOwnersAsync(ErpDbContext db, string ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken ct)
    {
        if (ownerIds.Count == 0) return;
        var rows = await db.Set<DocumentAttachment>().AsNoTracking()
            .Where(a => a.OwnerType == ownerType && ownerIds.Contains(a.OwnerId))
            .Select(a => new { a.Id, a.TenantId, a.StorageProvider }).ToListAsync(ct);
        if (rows.Any(r => r.StorageProvider != AttachmentStorageProviders.Database))
            throw new InvalidOperationException("حذف مرفقات مزود تخزين خارجي غير مدعوم بعد.");
        var ids = rows.Select(r => r.Id).ToList();
        var contents = await db.Set<DocumentAttachmentContent>().AsNoTracking().Where(c => ids.Contains(c.AttachmentId))
            .Select(c => new { c.Id, c.TenantId }).ToListAsync(ct);
        foreach (var c in contents) db.Remove(new DocumentAttachmentContent { Id = c.Id, TenantId = c.TenantId });
        foreach (var r in rows) db.Remove(new DocumentAttachment { Id = r.Id, TenantId = r.TenantId });
    }

    // ---------- مساعدات ----------

    private static class Events
    {
        public const string Added = "DocumentAttachmentAdded";
        public const string Deleted = "DocumentAttachmentDeleted";
    }

    private sealed record AttachmentRef(Guid Id, Guid TenantId, string OwnerType, Guid OwnerId, string FileName, string ContentType,
        string StorageProvider, string StorageKey);

    private IQueryable<DocumentAttachment> Rows(string ownerType, Guid ownerId)
        => _db.Set<DocumentAttachment>().AsNoTracking().Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId);

    private async Task<AttachmentRef> FindAsync(Guid attachmentId, CancellationToken ct)
        => await _db.Set<DocumentAttachment>().AsNoTracking()
               .Where(a => a.Id == attachmentId)
               .Select(a => new AttachmentRef(a.Id, a.TenantId, a.OwnerType, a.OwnerId, a.FileName, a.ContentType, a.StorageProvider, a.StorageKey))
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(Messages.AttachmentNotFound);

    /// <summary>المعالج، ثم الوحدة في الاشتراك، ثم العرض على شاشته، ثم وجود المستند ونطاقه وما يملكه المستخدم عليه.</summary>
    private async Task<(IAttachmentOwner Owner, AttachmentOwnerAccess Access)> ResolveAsync(string ownerType, Guid ownerId, CancellationToken ct)
    {
        var key = (ownerType ?? string.Empty).Trim().ToLowerInvariant();
        if (!_owners.TryGetValue(key, out var owner)) throw new NotFoundException(Messages.AttachmentOwnerTypeUnknown);
        if (!(await _entitlements.GetAsync(ct)).Allows(owner.Module)) throw new ForbiddenException(Messages.AttachmentModuleNotLicensed);
        await _permissions.DemandAsync(owner.ScreenId, ScreenAction.View, ct: ct);
        return (owner, await owner.ResolveAsync(ownerId, ct));
    }

    private IAttachmentStorage Storage(string provider)
        => _storages.TryGetValue(provider, out var storage)
            ? storage
            : throw new InvalidOperationException($"مزود تخزين المرفقات غير مسجّل: {provider}");

    private Task LogAsync(string action, AttachmentOwnerAccess access, string fileName, CancellationToken ct)
        => _audit.LogAsync(action, access.AuditEntity, access.AuditLabel, fileName, ct);
}

/// <summary>مزود التخزين في قاعدة بيانات المنشأة: سطر <see cref="DocumentAttachmentContent"/> لكل مرفق، يُحفظ مع السجل في معاملته.</summary>
public class DbAttachmentStorage : IAttachmentStorage
{
    private readonly ErpDbContext _db;
    public DbAttachmentStorage(ErpDbContext db) => _db = db;

    public string Provider => AttachmentStorageProviders.Database;

    public Task<string> StoreAsync(Guid attachmentId, byte[] content, CancellationToken ct = default)
    {
        var row = new DocumentAttachmentContent { Id = Guid.NewGuid(), AttachmentId = attachmentId, Content = content };
        _db.Add(row);
        return Task.FromResult(row.Id.ToString());
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken ct = default)
    {
        var id = ParseKey(key);
        return await _db.Set<DocumentAttachmentContent>().AsNoTracking().Where(c => c.Id == id).Select(c => c.Content).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.AttachmentNotFound);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var id = ParseKey(key);
        var row = await _db.Set<DocumentAttachmentContent>().AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Id, c.TenantId })
            .FirstOrDefaultAsync(ct);
        if (row != null) _db.Remove(new DocumentAttachmentContent { Id = row.Id, TenantId = row.TenantId });
    }

    private static Guid ParseKey(string key)
        => Guid.TryParse(key, out var id) ? id : throw new NotFoundException(Messages.AttachmentNotFound);
}
