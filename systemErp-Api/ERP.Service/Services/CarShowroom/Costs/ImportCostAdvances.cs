using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// تسوية تكاليف الاستيراد المقدمة عبر سند التكاليف الإضافية: السطر المسحوب يخص أمر شراء السند وعملته، لا يتجاوز رصيد المستند،
/// وتكلفته بسعر صرف المستند وبلا ضريبة. المسوّى مشتق من أسطر السندات المعتمدة، فيُعاد حسابه بعد اعتماد سند أو إلغائه.
/// </summary>
internal static class ImportCostAdvances
{
    /// <summary>سطر مسحوب من مستند: رقم السطر في السند والمبلغ المسحوب بعملة المستند (قبل الضريبة).</summary>
    public readonly record struct Pull(int LineNo, Guid AdvanceId, decimal Amount);

    /// <summary>
    /// يتحقق من الأسطر المسحوبة ويُرجع مستنداتها. <paramref name="excludeVoucherId"/> = السند نفسه (مسودته لا تُحسب ضمن المسوّى).
    /// </summary>
    public static async Task<Dictionary<Guid, ImportCostAdvance>> ValidateAsync(ErpDbContext db, IReadOnlyList<Pull> pulls, Guid? orderId, string currency,
        Guid? excludeVoucherId, CancellationToken ct)
    {
        if (pulls.Count == 0) return new();
        var ids = pulls.Select(p => p.AdvanceId).Distinct().ToList();
        var advances = await db.Set<ImportCostAdvance>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var settled = await SettledAsync(db, ids, excludeVoucherId, ct);
        foreach (var group in pulls.GroupBy(p => p.AdvanceId))
        {
            var first = group.First();
            string Line(string message) => string.Format(Messages.LineMessage, first.LineNo, message);
            if (!advances.TryGetValue(group.Key, out var advance)) throw new ValidationFailedException(Line(Messages.ImportCostAdvanceNotFound));
            if (advance.Status is not (ImportCostAdvanceStatus.Open or ImportCostAdvanceStatus.PartiallySettled))
                throw new ValidationFailedException(Line(string.Format(Messages.ImportCostAdvanceNotOpen, advance.Number)));
            if (orderId != advance.ProcurementOrderId)
                throw new ValidationFailedException(Line(string.Format(Messages.ImportCostAdvanceOrderMismatch, advance.Number, advance.ProcurementOrderNumber)));
            if (!string.Equals(currency, advance.CurrencyCode, StringComparison.OrdinalIgnoreCase))
                throw new ValidationFailedException(Line(string.Format(Messages.ImportCostAdvanceCurrencyMismatch, advance.Number, advance.CurrencyCode)));
            if (group.Any(p => p.Amount <= 0)) throw new ValidationFailedException(Line(Messages.ChargeAmountMustBePositive));
            var remaining = advance.Amount - settled.GetValueOrDefault(advance.Id).Amount;
            if (group.Sum(p => p.Amount) > remaining)
                throw new ValidationFailedException(Line(string.Format(Messages.ImportCostAdvanceExceedsRemaining, advance.Number, remaining.ToString("0.00"))));
        }
        return advances;
    }

    /// <summary>
    /// تكلفة الجزء المسحوب بالعملة الأساسية: نسبته من تكلفة المستند، وما يُقفل رصيده يأخذ بقية التكلفة تماماً (لا فروق تقريب على حساب التسوية).
    /// </summary>
    public static decimal CostOf(ImportCostAdvance advance, decimal amount, decimal settledAmount, decimal settledCost)
        => settledAmount + amount >= advance.Amount
            ? advance.CostAmount - settledCost
            : DocumentPricing.Round(advance.CostAmount * amount / advance.Amount);

    /// <summary>المسوّى لكل مستند من أسطر السندات المعتمدة (بعملة المستند وبالأساسية).</summary>
    public static async Task<Dictionary<Guid, (decimal Amount, decimal Cost)>> SettledAsync(ErpDbContext db, IReadOnlyCollection<Guid> advanceIds,
        Guid? excludeVoucherId, CancellationToken ct)
    {
        var rows = await db.Set<LandedCostVoucherLine>().AsNoTracking()
            .Where(l => l.ImportCostAdvanceId != null && advanceIds.Contains(l.ImportCostAdvanceId.Value)
                && l.Voucher.Status == LandedCostVoucherStatus.Approved && (excludeVoucherId == null || l.VoucherId != excludeVoucherId))
            .GroupBy(l => l.ImportCostAdvanceId!.Value)
            .Select(g => new { Id = g.Key, Amount = g.Sum(l => l.Amount), Cost = g.Sum(l => l.CostAmount) }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => (r.Amount, r.Cost));
    }

    /// <summary>يعيد حساب المسوّى وحالة المستندات بعد اعتماد سند أو إلغائه. يحفظ المتصل.</summary>
    public static async Task RefreshAsync(ErpDbContext db, IReadOnlyCollection<Guid> advanceIds, CancellationToken ct)
    {
        if (advanceIds.Count == 0) return;
        var settled = await SettledAsync(db, advanceIds, null, ct);
        foreach (var advance in await db.Set<ImportCostAdvance>().Where(a => advanceIds.Contains(a.Id)).ToListAsync(ct))
        {
            var (amount, cost) = settled.GetValueOrDefault(advance.Id);
            advance.SettledAmount = amount; advance.SettledCost = cost;
            if (advance.Status is ImportCostAdvanceStatus.Draft or ImportCostAdvanceStatus.Cancelled) continue;
            advance.Status = amount <= 0 ? ImportCostAdvanceStatus.Open
                : amount >= advance.Amount ? ImportCostAdvanceStatus.Settled : ImportCostAdvanceStatus.PartiallySettled;
        }
    }
}
