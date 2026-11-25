using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مواقع توصيل العميل: موقع افتراضي فعال واحد (تعيين غيره يلغي السابق)، والمستخدم في بيان يُعطَّل بدل حذفه.
/// التعديلات تُسجَّل تلقائياً في سجل التدقيق (<c>AuditTrailInterceptor</c>).
/// </summary>
public class CustomerDeliveryLocationService : ICustomerDeliveryLocationService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    public CustomerDeliveryLocationService(ErpDbContext db, ITransactionRunner tx) { _db = db; _tx = tx; }

    public async Task<List<CustomerDeliveryLocationDto>> ListAsync(Guid customerId, bool includeInactive, CancellationToken ct = default)
    {
        var items = await _db.Set<CustomerDeliveryLocation>().AsNoTracking()
            .Where(l => l.CustomerId == customerId && (includeInactive || l.IsActive))
            .OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name).ToListAsync(ct);
        var ids = items.Select(l => (Guid?)l.Id).ToList();
        var used = await _db.Set<DeliveryNote>().AsNoTracking().Where(n => ids.Contains(n.DeliveryLocationId))
            .Select(n => n.DeliveryLocationId!.Value).Distinct().ToListAsync(ct);
        return items.Select(l => ToDto(l, used.Contains(l.Id))).ToList();
    }

    public Task<CustomerDeliveryLocationDto> CreateAsync(Guid customerId, SaveCustomerDeliveryLocationDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (!await _db.Set<Customer>().AnyAsync(c => c.Id == customerId, token)) throw new NotFoundException(Messages.CustomerNotFound);
            var location = new CustomerDeliveryLocation { CustomerId = customerId };
            await ApplyAsync(location, r, token);
            _db.Add(location);
            await _db.SaveChangesAsync(token);
            return ToDto(location, false);
        }, ct);

    public Task<CustomerDeliveryLocationDto> UpdateAsync(Guid customerId, Guid id, SaveCustomerDeliveryLocationDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var location = await LoadAsync(customerId, id, token);
            await ApplyAsync(location, r, token);
            await _db.SaveChangesAsync(token);
            return ToDto(location, await IsUsedAsync(id, token));
        }, ct);

    public Task<bool> DeleteAsync(Guid customerId, Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var location = await LoadAsync(customerId, id, token);
            var used = await IsUsedAsync(id, token);
            if (used) { location.IsActive = false; location.IsDefault = false; }
            else _db.Remove(location);
            await _db.SaveChangesAsync(token);
            return !used;
        }, ct);

    private async Task<CustomerDeliveryLocation> LoadAsync(Guid customerId, Guid id, CancellationToken ct)
        => await _db.Set<CustomerDeliveryLocation>().FirstOrDefaultAsync(l => l.Id == id && l.CustomerId == customerId, ct)
            ?? throw new NotFoundException(Messages.DeliveryLocationNotFound);

    private Task<bool> IsUsedAsync(Guid id, CancellationToken ct)
        => _db.Set<DeliveryNote>().AnyAsync(n => n.DeliveryLocationId == id, ct);

    private async Task ApplyAsync(CustomerDeliveryLocation l, SaveCustomerDeliveryLocationDto r, CancellationToken ct)
    {
        static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        if (string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Address))
            throw new ValidationFailedException(Messages.DeliveryLocationNameAddressRequired);
        if ((r.Latitude == null) != (r.Longitude == null) || r.Latitude is < -90 or > 90 || r.Longitude is < -180 or > 180)
            throw new ValidationFailedException(Messages.CoordinatesInvalid);
        if (r.IsDefault && r.IsActive)
        {
            // الافتراضي السابق يُلغى ويُحفظ قبل تعديل هذا الموقع (فهرس فريد على الافتراضي الفعال)
            var others = await _db.Set<CustomerDeliveryLocation>()
                .Where(x => x.CustomerId == l.CustomerId && x.Id != l.Id && x.IsDefault).ToListAsync(ct);
            if (others.Count > 0)
            {
                foreach (var o in others) o.IsDefault = false;
                await _db.SaveChangesAsync(ct);
            }
        }
        l.Name =r.Name.Trim(); l.Address = r.Address.Trim();
        l.City = Clean(r.City); l.District = Clean(r.District);
        l.Latitude = r.Latitude is decimal lat ? Math.Round(lat, 6) : null;
        l.Longitude = r.Longitude is decimal lng ? Math.Round(lng, 6) : null;
        l.ContactName = Clean(r.ContactName); l.ContactPhone = Clean(r.ContactPhone); l.Notes = Clean(r.Notes);
        l.IsActive = r.IsActive;
        l.IsDefault = r.IsDefault && r.IsActive;
    }

    private static CustomerDeliveryLocationDto ToDto(CustomerDeliveryLocation l, bool used) => new()
    {
        Id = l.Id, CustomerId = l.CustomerId, Name = l.Name, Address = l.Address, City = l.City, District = l.District,
        Latitude = l.Latitude, Longitude = l.Longitude, ContactName = l.ContactName, ContactPhone = l.ContactPhone, Notes = l.Notes,
        IsDefault = l.IsDefault, IsActive = l.IsActive, IsUsed = used,
    };
}
