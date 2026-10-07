using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المتبقي على الفاتورة الآجلة = جزؤها الآجل عند الترحيل − ما وُزِّع عليها من سندات − الجزء الآجل من مرتجعاتها المرحّلة.
/// مصدر واحد لحساب المتبقي يستعمله السداد وروابط الدفع وأعمار الديون.
/// </summary>
internal static class InvoiceBalances
{
    /// <summary>الجزء الذي قُيِّد على حساب الطرف (آجل) من إجمالي المستند.</summary>
    public static decimal CreditPortion(PaymentMethod method, bool isSplit, decimal grandTotal, IEnumerable<(PaymentMethod Method, decimal Amount)> splits)
        => isSplit ? splits.Where(s => s.Method == PaymentMethod.Credit).Sum(s => s.Amount) : method == PaymentMethod.Credit ? grandTotal : 0;

    public sealed record Balance(decimal CreditAmount, decimal AmountDue);

    /// <param name="excludeVoucherId">سند يُعدَّل الآن: توزيعاته الحالية لا تُحسب عليه.</param>
    public static async Task<Dictionary<Guid, Balance>> ForAsync(ErpDbContext db, IReadOnlyCollection<Guid> invoiceIds, Guid? excludeVoucherId, CancellationToken ct)
    {
        if (invoiceIds.Count == 0) return new();
        var documents = await db.Set<Invoice>().AsNoTracking()
            .Where(i => invoiceIds.Contains(i.Id) || (i.OriginalInvoiceId != null && invoiceIds.Contains(i.OriginalInvoiceId.Value) && i.Status == "posted"))
            .Select(i => new { i.Id, i.OriginalInvoiceId, i.PaymentMethod, i.IsSplitPayment, i.GrandTotal }).ToListAsync(ct);
        var documentIds = documents.Select(d => d.Id).ToList();
        var splits = (await db.Set<InvoicePaymentSplit>().AsNoTracking().Where(s => documentIds.Contains(s.InvoiceId))
                .Select(s => new { s.InvoiceId, s.Method, s.Amount }).ToListAsync(ct))
            .ToLookup(s => s.InvoiceId, s => (s.Method, s.Amount));
        var allocated = (await db.Set<VoucherAllocation>().AsNoTracking()
                .Where(a => invoiceIds.Contains(a.InvoiceId) && (excludeVoucherId == null || a.VoucherId != excludeVoucherId))
                .GroupBy(a => a.InvoiceId).Select(g => new { g.Key, Sum = g.Sum(a => a.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);

        decimal Credit(Guid id) { var d = documents.First(x => x.Id == id); return CreditPortion(d.PaymentMethod, d.IsSplitPayment, d.GrandTotal, splits[id]); }

        var result = new Dictionary<Guid, Balance>();
        foreach (var id in invoiceIds.Where(id => documents.Any(d => d.Id == id)))
        {
            var credit = Credit(id);
            var returned = documents.Where(d => d.OriginalInvoiceId == id).Sum(d => Credit(d.Id));
            result[id] = new Balance(credit, Math.Max(0, credit - allocated.GetValueOrDefault(id) - returned));
        }
        return result;
    }

    public static async Task<decimal> DueAsync(ErpDbContext db, Guid invoiceId, CancellationToken ct)
        => (await ForAsync(db, new[] { invoiceId }, null, ct)).GetValueOrDefault(invoiceId)?.AmountDue ?? 0;
}
