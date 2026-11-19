using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ERP.Service.Services.Shared;

/// <summary>خطّاف يُستدعى من <see cref="ErpDbContext"/> قبل كل حفظ (يعيّنه حاوي الخدمات لكل طلب).</summary>
public interface IDocumentApprovalHook
{
    Task BeforeSaveAsync(ErpDbContext db, CancellationToken ct);
}

/// <summary>مستند قُدِّم للاعتماد في الطلب الحالي (لرسالة «أُرسل للاعتماد إلى...» في استجابته).</summary>
public sealed record SubmittedForApproval(string DocumentType, string ScreenId, string DocumentNumber);

/// <summary>
/// متابعة اعتماد المستندات من شاشاتها دون تعديل كل خدمة: قبل الحفظ يفحص تغيّر حالة كل مستند مسجّل في
/// <see cref="ApprovalTrackedDocuments"/>؛ دخوله «بانتظار الاعتماد» يفتح سجل متابعة ويُشعر من يملك «اعتماد» على شاشته،
/// وخروجه منها (اعتماد/رفض/إرجاع/إلغاء/حذف) يغلق السجل ويُشعر مقدّمه بالنتيجة. كله ضمن الحفظ نفسه (المعاملة نفسها).
/// </summary>
public sealed class DocumentApprovalTracker : IDocumentApprovalHook
{
    private readonly ICurrentUser _user;
    private readonly List<SubmittedForApproval> _submitted = new();
    private bool _running;

    public DocumentApprovalTracker(ICurrentUser user) => _user = user;

    /// <summary>ما قُدِّم للاعتماد خلال هذا الطلب.</summary>
    public IReadOnlyList<SubmittedForApproval> Submitted => _submitted;

    public async Task BeforeSaveAsync(ErpDbContext db, CancellationToken ct)
    {
        if (_running) return; // استعلامات الخطّاف لا تحفظ، لكن احتياطاً من أي حفظ متداخل
        _running = true;
        try
        {
            var changes = db.ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .Select(e => (Entry: e, Doc: ApprovalTrackedDocuments.For(e.Metadata.ClrType)))
                .Where(x => x.Doc != null)
                .ToList();
            foreach (var (entry, doc) in changes)
                await TrackAsync(db, entry, doc!, ct);
        }
        finally
        {
            _running = false;
        }
    }

    private async Task TrackAsync(ErpDbContext db, EntityEntry entry, ApprovalTrackedDocument doc, CancellationToken ct)
    {
        if (entry.Entity is not BaseEntity entity || entity.Id == Guid.Empty) return;
        if (entry.State == EntityState.Modified && !doc.StatusProperties.Any(p => entry.Property(p).IsModified)) return;
        var now = entry.State == EntityState.Deleted ? null : doc.State(p => entry.Property(p).CurrentValue?.ToString());
        var before = entry.State == EntityState.Added ? null : doc.State(p => entry.Property(p).OriginalValue?.ToString());
        if (entry.State == EntityState.Modified && string.Equals(now, before, StringComparison.OrdinalIgnoreCase)) return;

        var nowKind = doc.Classify(now);
        var beforeKind = doc.Classify(before);
        if (nowKind == DocumentApprovalTrackStatuses.Pending && beforeKind != DocumentApprovalTrackStatuses.Pending)
            await SubmitAsync(db, doc, entity, ct);
        else if (beforeKind == DocumentApprovalTrackStatuses.Pending && nowKind != DocumentApprovalTrackStatuses.Pending)
            await DecideAsync(db, doc, entity.Id, nowKind ?? DocumentApprovalTrackStatuses.Cancelled, ct);
    }

    private async Task<DocumentApprovalTrack?> FindAsync(ErpDbContext db, string type, Guid id, CancellationToken ct)
        => db.ChangeTracker.Entries<DocumentApprovalTrack>().Select(e => e.Entity).FirstOrDefault(t => t.DocumentType == type && t.DocumentId == id)
           ?? await db.Set<DocumentApprovalTrack>().FirstOrDefaultAsync(t => t.DocumentType == type && t.DocumentId == id, ct);

    private async Task SubmitAsync(ErpDbContext db, ApprovalTrackedDocument doc, BaseEntity entity, CancellationToken ct)
    {
        var me = _user.UserId;
        var requester = doc.Requester?.Invoke(entity) ?? (entity as IScopedRecord)?.CreatedByUserId ?? me;
        var requesterName = requester == me && !string.IsNullOrWhiteSpace(_user.Name) ? _user.Name!
            : requester is Guid rid ? await db.Set<User>().AsNoTracking().Where(u => u.Id == rid).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? string.Empty
            : string.Empty;
        var number = doc.Number(entity) ?? string.Empty;

        var track = await FindAsync(db, doc.Key, entity.Id, ct);
        if (track == null)
        {
            track = new DocumentApprovalTrack { DocumentType = doc.Key, DocumentId = entity.Id };
            db.Add(track);
        }
        track.DocumentNumber = number;
        track.Amount = doc.Amount?.Invoke(entity);
        track.ScreenId = doc.ScreenId;
        track.Url = Truncate(doc.Url(entity), 400);
        track.RequesterUserId = requester;
        track.RequesterName = requesterName;
        track.SubmittedAt = DateTime.UtcNow;
        track.Status = DocumentApprovalTrackStatuses.Pending;
        track.DecidedByUserId = null;
        track.DecidedByName = null;
        track.DecidedAt = null;

        // من حفظه يملك اعتماده بنفسه (يُنشئ ثم يعتمد): لا يُزعج بقية المعتمدين؛ يبقى السجل في «مستنداته» حتى يعتمده
        var store = new PermissionStore(db);
        List<Guid> approvers;
        try
        {
            if (me is Guid self && await CanApproveAsync(store, self, doc.ScreenId, ct)) return;
            approvers = await store.UsersWithAnyActionAsync(doc.ScreenId, new[] { ScreenAction.Approve }, ct);
        }
        catch (ErpException)
        {
            return; // دور مخصّص محذوف أو ما شابه: الإشعار إضافي ولا يُفشل حفظ المستند
        }

        // من يملك «اعتماد» على الشاشة يُشعَر بمستند ينتظره (عدا مقدّمه ومن حفظه الآن)
        var title = $"{doc.NameAr} بانتظار اعتمادك";
        var body = $"{doc.NameAr} رقم {number} قدّمه {requesterName} وينتظر الاعتماد.";
        foreach (var userId in approvers.Where(u => u != requester && u != me).Distinct())
            db.Add(new AppNotification
            {
                RecipientUserId = userId, Type = "doc_approval_request", Title = title, Body = body,
                RelatedDocType = doc.Key, RelatedDocId = entity.Id, RelatedDocNumber = number,
            });

        if (me != null) _submitted.Add(new SubmittedForApproval(doc.Key, doc.ScreenId, number));
    }

    private static async Task<bool> CanApproveAsync(PermissionStore store, Guid userId, string screenId, CancellationToken ct)
    {
        var subject = await store.SubjectAsync(userId, ct);
        if (subject is not { IsActive: true, RoleActive: true } || subject.RoleCode == PermissionService.EmployeeRoleId) return false;
        return (await store.GrantsForAsync(subject, ct)).Allows(screenId, ScreenAction.Approve);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private async Task DecideAsync(ErpDbContext db, ApprovalTrackedDocument doc, Guid documentId, string outcome, CancellationToken ct)
    {
        var track = await FindAsync(db, doc.Key, documentId, ct);
        if (track == null || track.Status != DocumentApprovalTrackStatuses.Pending) return;
        track.Status = outcome;
        track.DecidedByUserId = _user.UserId;
        track.DecidedByName = _user.Name;
        track.DecidedAt = DateTime.UtcNow;

        // مقدّمه يُبلَّغ بالنتيجة (لا عند الإلغاء أو حين يقرّر هو نفسه)
        if (!doc.NotifyRequester || outcome == DocumentApprovalTrackStatuses.Cancelled
            || track.RequesterUserId is not Guid requester || requester == _user.UserId) return;
        var (type, title) = outcome switch
        {
            DocumentApprovalTrackStatuses.Approved => ("doc_approval_approved", $"تم اعتماد {doc.NameAr}"),
            DocumentApprovalTrackStatuses.Rejected => ("doc_approval_rejected", $"تم رفض {doc.NameAr}"),
            _ => ("doc_approval_returned", $"أُعيد {doc.NameAr} للتعديل"),
        };
        db.Add(new AppNotification
        {
            RecipientUserId = requester, Type = type, Title = title,
            Body = $"{doc.NameAr} رقم {track.DocumentNumber} — بواسطة {_user.Name}.",
            RelatedDocType = doc.Key, RelatedDocId = documentId, RelatedDocNumber = track.DocumentNumber,
        });
    }
}
