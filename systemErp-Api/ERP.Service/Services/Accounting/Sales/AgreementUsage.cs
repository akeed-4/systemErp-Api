using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// استهلاك بنود الاتفاقيات: الكمية المستهلكة تُشتق دائماً من الأوامر المعتمدة المرتبطة بالاتفاقية
/// (لا عدّاد يُزاد ويُنقص) فتبقى صحيحة مهما عُدِّلت الأوامر أو أُلغيت أو حُذفت.
/// </summary>
internal static class AgreementUsage
{
    /// <summary>حالات الأمر التي تستهلك من رصيد الاتفاقية.</summary>
    public static readonly string[] ConsumingStatuses = { "confirmed", "partially_fulfilled", "completed" };

    public static bool IsConsuming(string status) => ConsumingStatuses.Contains(status);

    /// <summary>الكمية المستهلكة لكل صنف بأوامر معتمدة، مع استثناء أمر بعينه (عند تعديله).</summary>
    public static async Task<Dictionary<Guid, decimal>> UsedByItemAsync(ErpDbContext db, Guid agreementId, Guid? excludeOrderId, CancellationToken ct)
        => await db.Set<CommercialOrder>()
            .Where(o => o.AgreementId == agreementId && ConsumingStatuses.Contains(o.Status) && o.Id != excludeOrderId)
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ItemId)
            .Select(g => new { g.Key, Qty = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    /// <summary>يعيد احتساب UsedQuantity لبنود الاتفاقية من الأوامر المحفوظة.</summary>
    public static async Task RefreshAsync(ErpDbContext db, Guid agreementId, CancellationToken ct)
    {
        var agreement = await db.Set<Agreement>().Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == agreementId, ct);
        if (agreement == null) return;
        var used = await UsedByItemAsync(db, agreementId, null, ct);
        foreach (var item in agreement.Items) item.UsedQuantity = used.GetValueOrDefault(item.ItemId);
        await db.SaveChangesAsync(ct);
    }
}