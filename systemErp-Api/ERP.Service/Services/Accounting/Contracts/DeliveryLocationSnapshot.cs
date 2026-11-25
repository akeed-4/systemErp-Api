using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// لقطة موقع التوصيل على البيان: الموقع المختار يجب أن يكون موقعاً فعالاً لعميل البيان، والعنوان والإحداثيات الفارغة تُعبّأ منه.
/// تغيير اللقطة يمسح مراجعة الموقع (تلزم مراجعة جديدة قبل الخروج).
/// </summary>
internal static class DeliveryLocationSnapshot
{
    public static async Task ApplyAsync(ErpDbContext db, DeliveryNote note, CancellationToken ct)
    {
        note.DeliveryAddress = string.IsNullOrWhiteSpace(note.DeliveryAddress) ? null : note.DeliveryAddress.Trim();
        if (note.DeliveryLocationId is Guid locationId)
        {
            var location = await db.Set<CustomerDeliveryLocation>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
            var unchanged = db.Entry(note).State is not (EntityState.Added or EntityState.Detached)
                && Equals(db.Entry(note).Property(n => n.DeliveryLocationId).OriginalValue, locationId);
            // موقع معطّل بعد اختياره يبقى على البيان القائم، ولا يُختار لبيان جديد
            if (location == null || location.CustomerId != note.PartyId || (!location.IsActive && !unchanged))
                throw new ValidationFailedException(Messages.DeliveryLocationInvalid);
            note.DeliveryAddress ??= location.Address;
            if (note.DeliveryLatitude == null && note.DeliveryLongitude == null)
                (note.DeliveryLatitude, note.DeliveryLongitude) = (location.Latitude, location.Longitude);
        }
        if ((note.DeliveryLatitude == null) != (note.DeliveryLongitude == null)
            || note.DeliveryLatitude is < -90 or > 90 || note.DeliveryLongitude is < -180 or > 180)
            throw new ValidationFailedException(Messages.CoordinatesInvalid);
        if (note.DeliveryLatitude is decimal lat) note.DeliveryLatitude = Math.Round(lat, 6);
        if (note.DeliveryLongitude is decimal lng) note.DeliveryLongitude = Math.Round(lng, 6);

        var entry = db.Entry(note);
        var changed = entry.State == EntityState.Added
            || !Equals(entry.Property(n => n.DeliveryLocationId).OriginalValue, note.DeliveryLocationId)
            || !Equals(entry.Property(n => n.DeliveryAddress).OriginalValue, note.DeliveryAddress)
            || !Equals(entry.Property(n => n.DeliveryLatitude).OriginalValue, note.DeliveryLatitude)
            || !Equals(entry.Property(n => n.DeliveryLongitude).OriginalValue, note.DeliveryLongitude);
        if (changed && entry.State != EntityState.Detached)
            (note.LocationReviewedAt, note.LocationReviewedByUserId, note.LocationReviewedByName, note.LocationReviewNotes) = (null, null, null, null);
    }

    /// <summary>للبيان موقع توصيل (عنوان أو إحداثيات) يُراجَع.</summary>
    public static bool HasLocation(DeliveryNote note)
        => !string.IsNullOrWhiteSpace(note.DeliveryAddress) || note.DeliveryLatitude != null;
}
