using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class NotificationService : INotificationService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;

    public NotificationService(ErpDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
    }

    public async Task<AppNotificationDto> NotifyAsync(NotifyRequestDto r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw new ValidationFailedException("عنوان الإشعار مطلوب.");
        if (!await _db.Set<User>().AnyAsync(u => u.Id == r.RecipientUserId, ct))
            throw new NotFoundException("المستلم غير موجود");

        var n = new AppNotification
        {
            RecipientUserId = r.RecipientUserId, RecipientRole = r.RecipientRole, Title = r.Title, Body = r.Body, Type = r.Type,
            RelatedDocType = r.RelatedDocType, RelatedDocId = r.RelatedDocId, RelatedDocNumber = r.RelatedDocNumber,
        };
        _db.Add(n);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<AppNotificationDto>(n);
    }

    public async Task<List<AppNotificationDto>> ListMineAsync(CancellationToken ct = default)
    {
        var id = _user.UserId ?? throw new UnauthorizedAppException();
        var items = await _db.Set<AppNotification>().AsNoTracking().Where(n => n.RecipientUserId == id)
            .OrderByDescending(n => n.CreatedAt).Take(200).ToListAsync(ct);
        return items.Select(Mapper.Map<AppNotificationDto>).ToList();
    }

    public async Task<AppNotificationDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        return Mapper.Map<AppNotificationDto>(await _db.Set<AppNotification>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == me, ct)
            ?? throw new NotFoundException("الإشعار غير موجود"));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var n = await _db.Set<AppNotification>().FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == me, ct)
            ?? throw new NotFoundException("الإشعار غير موجود");
        _db.Remove(n);
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAsReadAsync(Guid id, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var n = await _db.Set<AppNotification>().FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == me, ct)
            ?? throw new NotFoundException("الإشعار غير موجود");
        n.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllAsReadAsync(CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        foreach (var n in await _db.Set<AppNotification>().Where(x => x.RecipientUserId == me && !x.IsRead).ToListAsync(ct))
            n.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        _db.RemoveRange(await _db.Set<AppNotification>().Where(x => x.RecipientUserId == me).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }
}
