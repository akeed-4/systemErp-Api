using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// متابعة التوصيل (شاشة <c>delivery-tracking</c>، وصلاحية لكل إجراء: عرض، خروج «إرسال»، تأكيد الاستلام «استلام»، إلغاء)
/// والتقارير (شاشة <c>delivery-reports</c>). كل إجراء بقفل البيان داخل معاملة، ويُسجَّل في سجل أحداثه.
/// لا فاتورة: الأثر المالي والمخزني بالفاتورة (مسار الاتفاقية) أو حدث سابقاً في فاتورة البيع، عدا بيان الفاتورة «غير المسلّمة»:
/// الإلغاء والتأكيد بأقل يعيدان ما خرج للمخزون (<see cref="DeferredDeliveryStock"/>).
/// </summary>
public class DeliveryTrackingService : IDeliveryTrackingService
{
    public const string ScreenId = PermissionCatalog.DeliveryTrackingScreenId;
    public const string ReportsScreenId = PermissionCatalog.DeliveryReportsScreenId;

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _user;


    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;

    public DeliveryTrackingService(ErpDbContext db, ITransactionRunner tx, IPermissionService permissions, ICurrentUser user,
        IInventoryService inventory, IAccountingPostingService posting)
    {
        _db = db; _tx = tx; _permissions = permissions; _user = user; _inventory = inventory; _posting = posting;
    }

    private async Task RequireAsync(string screen, ScreenAction action, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(screen, action, ct)) throw new ForbiddenException();
    }

    public async Task<PagedResult<DeliveryNoteDto>> ListAsync(DeliveryTrackingQueryDto q, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        var query = _db.Set<DeliveryNote>().AsNoTracking().Where(n => n.DeliveryStatus != DeliveryTrackingStatuses.Legacy);
        if (!string.IsNullOrWhiteSpace(q.Status)) query = query.Where(n => n.DeliveryStatus == q.Status);
        if (!string.IsNullOrWhiteSpace(q.SourceType)) query = query.Where(n => n.SourceType == q.SourceType);
        if (q.From is DateTime from) query = query.Where(n => n.Date >= from.Date);
        if (q.To is DateTime to) query = query.Where(n => n.Date < to.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var t = q.Search.Trim();
            query = query.Where(n => n.DeliveryNumber.Contains(t) || n.PartyName.Contains(t) || (n.SourceNumber != null && n.SourceNumber.Contains(t))
                || (n.DriverName != null && n.DriverName.Contains(t)));
        }
        var size = Math.Clamp(q.PageSize, 1, 200);
        var page = Math.Max(1, q.Page);
        var total = await query.CountAsync(ct);
        var items = await query.Include(n => n.Items).OrderByDescending(n => n.Date).ThenByDescending(n => n.CreatedAt)
            .Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<DeliveryNoteDto>
        {
            Items = items.Select(Mapper.Map<DeliveryNoteDto>).ToList(), TotalCount = total, PageNumber = page, PageSize = size,
        };
    }

    /// <summary>البيان المتتبَّع بقفله (تأكيد/إلغاء/فوترة/مرتجع متزامنة على البيان نفسه تُنفَّذ بالتتابع).</summary>
    private async Task<DeliveryNote> LoadLockedAsync(Guid id, CancellationToken ct)
    {
        await DeliverySources.LockNoteAsync(_db, id, ct);
        var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
        if (!DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus)) throw new ConflictException(Messages.DeliveryNoteNotTracked);
        return note;
    }

    private static void RequireStatus(DeliveryNote note, params string[] allowed)
    {
        if (!allowed.Contains(note.DeliveryStatus))
            throw new ConflictException(string.Format(Messages.DeliveryStatusTransitionInvalid, note.DeliveryStatus));
    }

    public Task<DeliveryNoteDto> DispatchAsync(Guid id, DispatchDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, ScreenAction.Send, token);
            var note = await LoadLockedAsync(id, token);
            RequireStatus(note, DeliveryTrackingStatuses.Pending);
            // مراجعة الموقع إلزامية بإعداد المنشأة
            if (note.LocationReviewedAt == null
                && await _db.Set<Tenant>().AsNoTracking().Where(t => t.Id == _db.CurrentTenantId).Select(t => t.RequireDeliveryLocationReview).FirstOrDefaultAsync(token))
                throw new ConflictException(Messages.DeliveryLocationReviewRequired);
            var from = note.DeliveryStatus;
            note.DeliveryStatus = DeliveryTrackingStatuses.InTransit;
            note.DispatchedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(r.DriverName)) note.DriverName = r.DriverName.Trim();
            if (!string.IsNullOrWhiteSpace(r.VehiclePlate)) note.VehiclePlate = r.VehiclePlate.Trim();
            DeliverySources.AddEvent(_db, note, _user, "dispatched", from, note.DeliveryStatus, r.Notes);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<DeliveryNoteDto>(note);
        }, ct);

    /// <summary>مراجعة موقع التوصيل قبل الخروج (إجراء «مراجعة»). تغيير اللقطة لاحقاً يمسحها.</summary>
    public Task<DeliveryNoteDto> ReviewLocationAsync(Guid id, ReviewDeliveryLocationDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, ScreenAction.Review, token);
            var note = await LoadLockedAsync(id, token);
            RequireStatus(note, DeliveryTrackingStatuses.Pending);
            if (!DeliveryLocationSnapshot.HasLocation(note)) throw new ConflictException(Messages.DeliveryLocationMissing);
            note.LocationReviewedAt = DateTime.UtcNow;
            note.LocationReviewedByUserId = _user.UserId;
            note.LocationReviewedByName = _user.Name;
            note.LocationReviewNotes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
            DeliverySources.AddEvent(_db, note, _user, "location_reviewed", note.DeliveryStatus, note.DeliveryStatus, note.LocationReviewNotes);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<DeliveryNoteDto>(note);
        }, ct);

    /// <summary>
    /// تأكيد الاستلام نهائي للبيان: المؤكد لكل سطر ≤ كمية البيان، والنقص عن البيان يُحرَّر من المصدر (يُسلَّم ببيان آخر).
    /// </summary>
    public Task<DeliveryNoteDto> ConfirmAsync(Guid id, ConfirmDeliveryDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, ScreenAction.Receive, token);
            if (string.IsNullOrWhiteSpace(r.ReceiverName)) throw new ValidationFailedException(Messages.DeliveryReceiverRequired);
            if (r.Lines.Any(l => l.ConfirmedQty < 0)) throw new ValidationFailedException(Messages.QuantitiesCannotBeNegative);
            var note = await LoadLockedAsync(id, token);
            RequireStatus(note, DeliveryTrackingStatuses.Pending, DeliveryTrackingStatuses.InTransit);
            if (note.Status == DeliveryNoteStatus.Cancelled) throw new ConflictException(Messages.DeliveryNoteCancelled);
            if (r.Lines.Any(l => note.Items.All(i => i.Id != l.LineId))) throw new ValidationFailedException(Messages.ItemNotInDeliveryNote);

            var sent = r.Lines.GroupBy(l => l.LineId).ToDictionary(g => g.Key, g => g.Sum(l => l.ConfirmedQty));
            foreach (var i in note.Items)
            {
                var qty = sent.GetValueOrDefault(i.Id);
                if (qty > i.DeliveredQty + DeliverySources.Tolerance)
                    throw new ValidationFailedException(string.Format(Messages.DeliveryConfirmExceedsNote, i.ItemName, qty.ToString("0.####"), i.DeliveredQty.ToString("0.####")));
                i.ConfirmedQty = qty;
            }
            if (note.Items.All(i => i.ConfirmedQty <= 0)) throw new ValidationFailedException(Messages.DeliveryConfirmNothing);

            var from = note.DeliveryStatus;
            note.DeliveryStatus = note.Items.All(i => i.ConfirmedQty >= i.DeliveredQty - DeliverySources.Tolerance)
                ? DeliveryTrackingStatuses.Delivered : DeliveryTrackingStatuses.PartiallyDelivered;
            note.DeliveredAt = r.DeliveredAt ?? DateTime.UtcNow;
            note.DeliveryConfirmedByUserId = _user.UserId;
            note.ReceiverName = r.ReceiverName.Trim();
            DeliverySources.AddEvent(_db, note, _user, note.DeliveryStatus, from, note.DeliveryStatus,
                string.IsNullOrWhiteSpace(r.Notes) ? $"المستلم: {note.ReceiverName}" : $"المستلم: {note.ReceiverName} — {r.Notes}");
            await _db.SaveChangesAsync(token);
            // فاتورة «غير مسلّمة»: النقص المؤكد عن البيان يعود للمخزون (يُسلَّم ببيان آخر)
            await DeferredDeliveryStock.SyncAsync(_db, _inventory, _posting, note, removing: false, token);
            return Mapper.Map<DeliveryNoteDto>(note);
        }, ct);

    /// <summary>
    /// الإلغاء قبل التسليم الفعلي والفوترة والمرتجع فقط، بسبب إلزامي. الملغى لا يحجز كمياته (تعود متاحة من المصدر) ولا يُحذف.
    /// </summary>
    public Task<DeliveryNoteDto> CancelAsync(Guid id, CancelDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, ScreenAction.Cancel, token);
            if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException(Messages.DeliveryCancelReasonRequired);
            var note = await LoadLockedAsync(id, token);
            if (note.DeliveryStatus is not (DeliveryTrackingStatuses.Pending or DeliveryTrackingStatuses.InTransit)
                || note.InvoiceId != null || note.Items.Any(i => i.InvoicedQty > 0 || i.ReturnedQty > 0 || i.ConfirmedQty > 0)
                || await _db.Set<DeliveryNoteInvoiceLine>().AnyAsync(l => l.DeliveryNoteId == id, token)
                || await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token))
                throw new ConflictException(Messages.DeliveryCannotCancel);

            var from = note.DeliveryStatus;
            note.Status = DeliveryNoteStatus.Cancelled;
            note.DeliveryStatus = DeliveryTrackingStatuses.Cancelled;
            note.CancelledAt = DateTime.UtcNow;
            note.CancelledByUserId = _user.UserId;
            note.CancellationReason = r.Reason.Trim();
            DeliverySources.AddEvent(_db, note, _user, "cancelled", from, note.DeliveryStatus, note.CancellationReason);
            await _db.SaveChangesAsync(token);
            // فاتورة «غير مسلّمة»: ما خرج بالبيان يعود للمخزون وتُعكس تكلفته
            await DeferredDeliveryStock.SyncAsync(_db, _inventory, _posting, note, removing: false, token);
            return Mapper.Map<DeliveryNoteDto>(note);
        }, ct);

    public async Task<List<DeliveryNoteEventDto>> GetEventsAsync(Guid id, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        if (!await _db.Set<DeliveryNote>().AnyAsync(n => n.Id == id, ct)) throw new NotFoundException(Messages.DeliveryNoteNotFound);
        return await DeliverySources.EventsAsync(_db, id, ct);
    }

    public async Task<List<DeliveryStatusSummaryDto>> SummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await RequireAsync(ReportsScreenId, ScreenAction.View, ct);
        var q = _db.Set<DeliveryNote>().AsNoTracking().Where(n => n.DeliveryStatus != DeliveryTrackingStatuses.Legacy);
        if (from is DateTime f) q = q.Where(n => n.Date >= f.Date);
        if (to is DateTime t) q = q.Where(n => n.Date < t.Date.AddDays(1));
        return await q.GroupBy(n => new { n.SourceType, n.DeliveryStatus })
            .Select(g => new DeliveryStatusSummaryDto { SourceType = g.Key.SourceType, DeliveryStatus = g.Key.DeliveryStatus, Count = g.Count() })
            .OrderBy(x => x.SourceType).ThenBy(x => x.DeliveryStatus).ToListAsync(ct);
    }
}
