using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تنبيهات الخياطة داخل التطبيق (قوالب واتساب للعميل لاحقاً). القواعد:
/// <list type="bullet">
/// <item>جاهزية الأمر: حين تصبح كل قطعه جاهزة يُشعَر من يملك إنشاء التسليم.</item>
/// <item>الفحص اليومي (عند فتح اللوحة): ملخص واحد في اليوم للأوامر المتأخرة والمستحقة اليوم لمن يرى الأوامر، وملخص واحد لبروفات اليوم لمن
/// يسجل نتائجها. التكرار في اليوم نفسه يُتجاهل.</item>
/// <item>المنفّذ لا يُشعَر نفسه بالجاهزية، وتعذّر الإشعار لا يُسقط العملية.</item>
/// </list>
/// </summary>
public class TailoringAlertService : ITailoringAlertService
{
    public const string ReadyType = "tailoring_ready", LateType = "tailoring_late", FittingsType = "tailoring_fittings";
    private const int ListedMax = 15;

    private readonly ErpDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _user;

    public TailoringAlertService(ErpDbContext db, INotificationService notifications, IPermissionService permissions, ICurrentUser user)
    {
        _db = db; _notifications = notifications; _permissions = permissions; _user = user;
    }

    public async Task OrderReadyAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct);
            if (o == null) return;
            var me = _user.UserId;
            var users = (await _permissions.UsersWithAnyActionAsync(PermissionCatalog.TailoringDeliveryScreenId, new[] { ScreenAction.Create }, ct))
                .Where(id => id != me).ToList();
            if (users.Count == 0) return;
            await _notifications.NotifyUsersAsync(users, new NotifyRequestDto
            {
                Type = ReadyType, Title = string.Format(Messages.TailoringNotifyReadyTitle, o.OrderNumber),
                Body = string.Format(Messages.TailoringNotifyReadyBody, o.CustomerName, o.PieceCount),
                RelatedDocType = ReadyType, RelatedDocId = o.Id, RelatedDocNumber = o.OrderNumber,
            }, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested) { Discard(); }
    }

    public async Task ScanAsync(CancellationToken ct = default)
    {
        try
        {
            var today = SaudiTime.Today;
            var since = SaudiTime.ToUtc(today);
            var sent = await _db.Set<AppNotification>().AsNoTracking()
                .Where(n => (n.Type == LateType || n.Type == FittingsType) && n.CreatedAt >= since).Select(n => n.Type).Distinct().ToListAsync(ct);

            if (!sent.Contains(LateType))
            {
                var due = await _db.Set<TailoringOrder>().AsNoTracking()
                    .Where(o => St.Open.Contains(o.Status) && o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate <= today)
                    .OrderBy(o => o.ExpectedDeliveryDate).Select(o => new { o.OrderNumber, o.ExpectedDeliveryDate }).ToListAsync(ct);
                if (due.Count > 0)
                {
                    var late = due.Count(o => o.ExpectedDeliveryDate < today);
                    var users = await _permissions.UsersWithAnyActionAsync(PermissionCatalog.TailoringOrdersScreenId, new[] { ScreenAction.View }, ct);
                    await _notifications.NotifyUsersAsync(users, new NotifyRequestDto
                    {
                        Type = LateType, RelatedDocType = LateType,
                        Title = string.Format(Messages.TailoringNotifyLateTitle, late, due.Count - late),
                        Body = List(due.Select(o => o.OrderNumber).ToList()),
                    }, ct);
                }
            }

            if (!sent.Contains(FittingsType))
            {
                var tomorrow = today.AddDays(1);
                var fittings = await (from f in _db.Set<TailoringFitting>().AsNoTracking()
                                      join g in _db.Set<TailoringGarment>().AsNoTracking() on f.GarmentId equals g.Id
                                      where f.Status == TailoringFittingStatuses.Scheduled && f.ScheduledAt >= today && f.ScheduledAt < tomorrow
                                      orderby f.ScheduledAt
                                      select new { f.ScheduledAt, g.Code }).ToListAsync(ct);
                if (fittings.Count > 0)
                {
                    var users = await _permissions.UsersWithAnyActionAsync(PermissionCatalog.TailoringFittingsScreenId, new[] { ScreenAction.Edit }, ct);
                    await _notifications.NotifyUsersAsync(users, new NotifyRequestDto
                    {
                        Type = FittingsType, RelatedDocType = FittingsType, Title = string.Format(Messages.TailoringNotifyFittingsTitle, fittings.Count),
                        Body = List(fittings.Select(f => $"{f.ScheduledAt:HH:mm} {f.Code}").ToList()),
                    }, ct);
                }
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested) { Discard(); }
    }

    /// <summary>إشعار تعذّر حفظه لا يبقى معلّقاً في المتتبِّع فيُفشل حفظ العملية بعده.</summary>
    private void Discard()
    {
        foreach (var e in _db.ChangeTracker.Entries<AppNotification>().Where(e => e.State == EntityState.Added).ToList()) e.State = EntityState.Detached;
    }

    private static string List(List<string> items)
        => string.Join("، ", items.Take(ListedMax)) + (items.Count > ListedMax ? $" (+{items.Count - ListedMax})" : string.Empty);
}
