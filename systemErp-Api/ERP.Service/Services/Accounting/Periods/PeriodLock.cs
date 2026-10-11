using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

public class PeriodLock : IPeriodLock
{
    private readonly ErpDbContext _db;
    public PeriodLock(ErpDbContext db) => _db = db;

    /// <summary>يُقرأ من قاعدة البيانات عند كل فحص: الإقفال يسري فوراً حتى داخل الطلب الذي ضبطه.</summary>
    private Task<DateTime?> LockedThroughAsync(CancellationToken ct)
        => _db.Set<Tenant>().AsNoTracking().Select(t => t.BooksLockedThrough).FirstOrDefaultAsync(ct);

    public async Task<bool> IsOpenAsync(DateTime date, CancellationToken ct = default)
    {
        var locked = await LockedThroughAsync(ct);
        return locked == null || date.Date > locked.Value.Date;
    }

    public async Task EnsureOpenAsync(DateTime date, CancellationToken ct = default)
    {
        var locked = await LockedThroughAsync(ct);
        if (locked != null && date.Date <= locked.Value.Date)
            throw new ConflictException(string.Format(Messages.PeriodLocked, date, locked.Value));
    }
}
