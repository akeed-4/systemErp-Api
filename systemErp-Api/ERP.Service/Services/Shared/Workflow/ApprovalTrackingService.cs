using System.Globalization;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// قوائم متابعة اعتماد المستندات من شاشاتها: «مستنداتي بانتظار الاعتماد» (ما قدّمه المستخدم وحالته ومن يعتمده)،
/// و«بانتظار اعتمادي» (المعلّق على الشاشات التي يملك عليها «اعتماد»). السجلات يكتبها <see cref="DocumentApprovalTracker"/>.
/// </summary>
public class ApprovalTrackingService : IApprovalTrackingService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly DocumentApprovalTracker _tracker;
    private readonly Dictionary<string, ApproversDto> _approvers = new();

    public ApprovalTrackingService(ErpDbContext db, ICurrentUser user, IPermissionService permissions, DocumentApprovalTracker tracker)
    {
        _db = db; _user = user; _permissions = permissions; _tracker = tracker;
    }

    /// <summary>شاشات المستندات المتابَعة التي يملك المستخدم «اعتماد» عليها.</summary>
    private async Task<List<string>> ApprovableScreensAsync(CancellationToken ct)
    {
        var screens = new List<string>();
        foreach (var screen in ApprovalTrackedDocuments.All.Select(d => d.ScreenId).Distinct())
            if (await _permissions.HasPermissionAsync(screen, ScreenAction.Approve, ct)) screens.Add(screen);
        return screens;
    }

    private async Task<ApproversDto> ApproversAsync(string screenId, CancellationToken ct)
    {
        if (!_approvers.TryGetValue(screenId, out var a))
            _approvers[screenId] = a = await _permissions.GetApproversAsync(screenId, ScreenAction.Approve, null, ct);
        return a;
    }

    public async Task<PagedResult<ApprovalTrackItemDto>> ListAsync(string scope, string? status, PaginationParams p, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var q = _db.Set<DocumentApprovalTrack>().AsNoTracking().AsQueryable();
        var screens = await ApprovableScreensAsync(ct);
        if (string.Equals(scope, "awaiting", StringComparison.OrdinalIgnoreCase))
            q = q.Where(t => t.Status == DocumentApprovalTrackStatuses.Pending && screens.Contains(t.ScreenId));
        else
            q = q.Where(t => t.RequesterUserId == me);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var term = p.SearchTerm.Trim();
            q = q.Where(t => t.DocumentNumber.Contains(term) || t.RequesterName.Contains(term));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(t => t.SubmittedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);

        var items = new List<ApprovalTrackItemDto>();
        foreach (var t in rows)
        {
            var doc = ApprovalTrackedDocuments.Find(t.DocumentType);
            items.Add(new ApprovalTrackItemDto
            {
                Id = t.Id, DocumentType = t.DocumentType, DocumentId = t.DocumentId, DocumentNumber = t.DocumentNumber, Amount = t.Amount,
                DocumentNameAr = doc?.NameAr ?? t.DocumentType, DocumentNameEn = doc?.NameEn ?? t.DocumentType,
                ScreenId = t.ScreenId, RequesterUserId = t.RequesterUserId, RequesterName = t.RequesterName, SubmittedAt = t.SubmittedAt,
                Status = t.Status, DecidedByName = t.DecidedByName, DecidedAt = t.DecidedAt,
                Url = t.Url,
                ApproverRoles = t.Status == DocumentApprovalTrackStatuses.Pending ? (await ApproversAsync(t.ScreenId, ct)).Roles : new(),
                CanApprove = t.Status == DocumentApprovalTrackStatuses.Pending && screens.Contains(t.ScreenId),
            });
        }
        return new PagedResult<ApprovalTrackItemDto> { Items = items, TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize };
    }

    public async Task<ApprovalTrackSummaryDto> SummaryAsync(CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var screens = await ApprovableScreensAsync(ct);
        var pending = _db.Set<DocumentApprovalTrack>().AsNoTracking().Where(t => t.Status == DocumentApprovalTrackStatuses.Pending);
        return new ApprovalTrackSummaryDto
        {
            MinePending = await pending.CountAsync(t => t.RequesterUserId == me, ct),
            AwaitingMe = screens.Count == 0 ? 0 : await pending.CountAsync(t => screens.Contains(t.ScreenId), ct),
        };
    }

    public async Task<string?> SubmissionMessageAsync(CancellationToken ct = default)
    {
        if (_tracker.Submitted.Count == 0) return null;
        var english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var s in _tracker.Submitted.DistinctBy(s => (s.DocumentType, s.DocumentNumber)))
        {
            // من يملك اعتماده بنفسه لا يحتاج الرسالة
            if (await _permissions.HasPermissionAsync(s.ScreenId, ScreenAction.Approve, ct)) continue;
            var doc = ApprovalTrackedDocuments.Find(s.DocumentType);
            var roles = (await ApproversAsync(s.ScreenId, ct)).Roles.Select(r => english ? r.NameEn : r.NameAr);
            lines.Add(string.Format(Messages.DocApprovalSubmitted, doc?.Name ?? s.DocumentType, s.DocumentNumber, string.Join(Messages.ListSeparator, roles)));
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
