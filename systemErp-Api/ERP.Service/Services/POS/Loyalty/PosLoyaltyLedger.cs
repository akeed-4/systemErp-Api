using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.POS;

/// <summary>
/// نقاط الولاء المعلّقة: نقاط الفاتورة تُعلَّق حتى تنتهي مدة الترجيع ثم تُضاف للرصيد المتاح،
/// والمرتجع/الإلغاء قبلها يخصم من المعلّق أولاً ثم من الرصيد المتاح.
/// التزامن: الرصيد والقيد بـ RowVersion، والعمليتان المتزامنتان على أيهما تُعاد الثانية منهما على الحالة الجديدة (TransactionRunner).
/// </summary>
public static class PosLoyaltyLedger
{
    /// <summary>أيام التعليق: إعداد الولاء، وإلا مدة ترجيع المبيعات في المحاسبة، وإلا فوراً.</summary>
    public static async Task<int> PendingDaysAsync(ErpDbContext db, PosPromotionSettings rules, CancellationToken ct)
        => rules.PointsPendingDays
           ?? await db.Set<AccountingSettings>().AsNoTracking().Select(s => s.SalesReturnPeriodDays).FirstOrDefaultAsync(ct)
           ?? 0;

    /// <summary>
    /// نقاط مكتسبة لفاتورة: معلّقة بقيد إن كانت هناك مدة، وإلا في الرصيد مباشرة. قيد واحد لكل فاتورة (فهرس فريد):
    /// الاستدعاء المكرر للفاتورة نفسها لا يضيف شيئاً.
    /// </summary>
    public static async Task EarnAsync(ErpDbContext db, CustomerLoyalty loyalty, Guid transactionId, int points, PosPromotionSettings rules, CancellationToken ct)
    {
        if (points <= 0) return;
        var days = await PendingDaysAsync(db, rules, ct);
        if (days <= 0)
        {
            loyalty.PointsBalance += points;
            loyalty.TotalPointsEarned += points;
            return;
        }
        var exists = db.ChangeTracker.Entries<LoyaltyPointEntry>().Any(e => e.Entity.TransactionId == transactionId)
            || await db.Set<LoyaltyPointEntry>().AnyAsync(e => e.TransactionId == transactionId, ct);
        if (exists) return;
        db.Add(new LoyaltyPointEntry
        {
            CustomerId = loyalty.CustomerId, TransactionId = transactionId, Points = points, OriginalPoints = points,
            AvailableAt = DateTime.UtcNow.AddDays(days),
        });
        loyalty.PendingPoints += points;
    }

    /// <summary>
    /// إتاحة ما حلّ موعده (لعميل أو للجميع): يدخل الرصيد وإجمالي المكتسب وتُحدَّث القيمة والفئة.
    /// يعمل داخل معاملة المستدعي إن وُجدت، وإلا في معاملته الخاصة (يُعاد كاملاً عند تعارض التزامن فلا تُتاح النقاط مرتين).
    /// عميل بلا سجل ولاء: يُنشأ سجله؛ وعميل محذوف: تبقى نقاطه معلّقة ولا تضيع.
    /// </summary>
    public static Task ReleaseDueAsync(ErpDbContext db, ITransactionRunner tx, Guid? customerId, CancellationToken ct)
        => tx.RunAsync(async token =>
        {
            var now = DateTime.UtcNow;
            var due = await db.Set<LoyaltyPointEntry>()
                .Where(e => e.Status == LoyaltyPointEntryStatus.Pending && e.AvailableAt <= now && (customerId == null || e.CustomerId == customerId))
                .ToListAsync(token);
            if (due.Count == 0) return;
            var rules = await PosPricing.SettingsAsync(db, token);
            var ids = due.Select(e => e.CustomerId).Distinct().ToList();
            var accounts = await db.Set<CustomerLoyalty>().Where(l => ids.Contains(l.CustomerId)).ToDictionaryAsync(l => l.CustomerId, token);
            var missing = ids.Where(id => !accounts.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                foreach (var c in await db.Set<Customer>().AsNoTracking().Where(c => missing.Contains(c.Id)).ToListAsync(token))
                {
                    var created = new CustomerLoyalty { CustomerId = c.Id, CustomerName = c.NameAr, Phone = c.Phone };
                    db.Add(created);
                    accounts[c.Id] = created;
                }

            foreach (var e in due)
            {
                if (!accounts.TryGetValue(e.CustomerId, out var l)) continue; // عميل محذوف: يبقى القيد معلّقاً
                e.Status = LoyaltyPointEntryStatus.Released;
                l.PendingPoints = Math.Max(0, l.PendingPoints - e.Points);
                l.PointsBalance += e.Points;
                l.TotalPointsEarned += e.Points;
            }
            foreach (var l in accounts.Values) PosPricing.Revalue(l, rules);
            await db.SaveChangesAsync(token);
        }, ct);

    /// <summary>
    /// سحب نقاط فاتورة (مرتجع أو إلغاء) بحد ما بقي لها: مجموع المسحوب لا يتجاوز ما كسبته (LoyaltyPointsRevoked)،
    /// من المعلّق أولاً ثم من الرصيد المتاح (بحدّه). يعيد المسحوب فعلاً وما سُحب من المتاح.
    /// </summary>
    public static async Task<(int Revoked, int FromAvailable)> RevokeAsync(ErpDbContext db, CustomerLoyalty loyalty, PosTransaction sale, int points, CancellationToken ct)
    {
        var allowed = Math.Min(points, Math.Max(0, sale.PointsEarned - sale.LoyaltyPointsRevoked));
        if (allowed <= 0) return (0, 0);
        sale.LoyaltyPointsRevoked += allowed;

        var entry = await db.Set<LoyaltyPointEntry>()
            .FirstOrDefaultAsync(e => e.TransactionId == sale.Id && e.Status == LoyaltyPointEntryStatus.Pending, ct);
        var fromPending = Math.Min(entry?.Points ?? 0, allowed);
        if (entry != null && fromPending > 0)
        {
            entry.Points -= fromPending;
            if (entry.Points == 0) entry.Status = LoyaltyPointEntryStatus.Cancelled;
            loyalty.PendingPoints = Math.Max(0, loyalty.PendingPoints - fromPending);
        }
        var fromAvailable = Math.Min(loyalty.PointsBalance, allowed - fromPending);
        loyalty.PointsBalance -= fromAvailable;
        return (fromPending + fromAvailable, fromAvailable);
    }
}
