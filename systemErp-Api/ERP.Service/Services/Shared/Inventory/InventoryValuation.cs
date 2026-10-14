using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// القيمة الدفترية للمخزون: قيمة الصنف = رصيده × متوسط تكلفته مقرّبة لهللة، وحساب المخزون يتحرك بتغيّر هذه القيمة تماماً.
/// كل ما يغيّر تكلفة الأصناف دون حركة (إعادة احتساب، تغيير طريقة التكلفة، تغيير التكلفة المعيارية) يُرحَّل فرقه بقيد إعادة تقييم.
/// </summary>
internal static class InventoryValuation
{
    public const string RevaluationSource = "inventory_revaluation";

    public static decimal Value(Product p) => DocumentPricing.Round(p.CurrentStock * p.AverageCost);

    public static Dictionary<Guid, decimal> Snapshot(IEnumerable<Product> products) => products.ToDictionary(p => p.Id, Value);

    /// <summary>فرق القيمة بعد التغيير عن اللقطة السابقة، مجموعاً لكل الأصناف.</summary>
    public static decimal Difference(IEnumerable<Product> products, IReadOnlyDictionary<Guid, decimal> before)
        => products.Sum(p => Value(p) - before.GetValueOrDefault(p.Id));

    /// <summary>
    /// قيد إعادة تقييم المخزون: الزيادة مدين المخزون / دائن حساب فروق التكلفة (من سياسة التكلفة، أو فروقات الجرد)، والنقص عكسه.
    /// </summary>
    public static async Task<PostingResult?> PostRevaluationAsync(ErpDbContext db, IAccountingPostingService posting, decimal amount,
        string description, Guid? sourceId, CancellationToken ct)
    {
        if (amount == 0) return null;
        var configured = await db.Set<CostingPolicy>().AsNoTracking().Select(p => p.StandardCostVarianceAccountCode).FirstOrDefaultAsync(ct);
        var counter = string.IsNullOrWhiteSpace(configured) ? DefaultAccounts.InventoryAdjustment : configured.Trim();
        await DefaultAccounts.EnsureAsync(db, ct, DefaultAccounts.Inventory, counter);
        var value = Math.Abs(amount);
        return await posting.PostAsync(new GenericPostingRequest
        {
            Date = SaudiTime.Today, Description = description, SourceType = RevaluationSource, SourceId = sourceId,
            Lines = amount > 0
                ? new() { new(DefaultAccounts.Inventory, value, 0, description), new(counter, 0, value, description) }
                : new() { new(counter, value, 0, description), new(DefaultAccounts.Inventory, 0, value, description) },
        }, ct);
    }
}
