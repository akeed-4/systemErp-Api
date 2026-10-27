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
    /// قيم أصناف (أو تغيّر قيمها) موزّعة على حسابات مخزون مستودعاتها بنسبة كمياتها الموجبة فيها؛ الصنف بلا رصيد في أي مستودع
    /// على حساب المستودع الافتراضي. بلا حسابات للمستودعات: كلها على حساب المخزون في الإعدادات.
    /// <paramref name="excluded"/>: كمية حركة لا يشملها التغيّر (إعادة تسعير الرصيد السابق لها) تُطرح من وزن مستودعها.
    /// </summary>
    public static async Task<Dictionary<string, decimal>> ByAccountAsync(ErpDbContext db, AccountingSettings s,
        IEnumerable<(Guid ItemId, decimal Amount)> amounts, CancellationToken ct, (Guid WarehouseId, decimal Quantity)? excluded = null)
    {
        var list = amounts.Where(a => a.Amount != 0).ToList();
        var result = new Dictionary<string, decimal>();
        void Add(string account, decimal amount) => result[account] = result.GetValueOrDefault(account) + amount;
        if (list.Count == 0) return result;

        var warehouses = await db.Set<Warehouse>().AsNoTracking().OrderByDescending(w => w.IsDefault).ThenBy(w => w.CreatedAt)
            .Select(w => new { w.Id, w.InventoryAccountCode }).ToListAsync(ct);
        var fallback = warehouses.FirstOrDefault()?.InventoryAccountCode ?? s.Inventory();
        if (warehouses.All(w => w.InventoryAccountCode == null))
        {
            Add(fallback, list.Sum(a => a.Amount));
            return result;
        }
        var accountOf = warehouses.ToDictionary(w => w.Id, w => w.InventoryAccountCode ?? s.Inventory());
        var ids = list.Select(a => a.ItemId).Distinct().ToList();
        var stocks = (await db.Set<WarehouseStock>().Where(x => ids.Contains(x.ItemId)).ToListAsync(ct))
            .Select(x => (x.ItemId, x.WarehouseId, Quantity: x.Quantity - (excluded is { } e && e.WarehouseId == x.WarehouseId ? e.Quantity : 0)))
            .Where(x => x.Quantity > 0)
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.WarehouseId).ToList());
        foreach (var (itemId, amount) in list)
        {
            if (!stocks.TryGetValue(itemId, out var rows)) { Add(fallback, amount); continue; }
            var total = rows.Sum(r => r.Quantity);
            var left = amount;
            for (var i = 0; i < rows.Count; i++)
            {
                var share = i == rows.Count - 1 ? left : DocumentPricing.Round(amount * rows[i].Quantity / total); // الباقي على الأخير فلا تضيع هللة
                left -= share;
                Add(accountOf.GetValueOrDefault(rows[i].WarehouseId, fallback), share);
            }
        }
        return result;
    }

    /// <summary>
    /// قيد إعادة تقييم المخزون: الزيادة مدين المخزون / دائن حساب فروق التكلفة (من سياسة التكلفة، أو فروقات الجرد)، والنقص عكسه.
    /// تغيّر قيمة كل صنف يُوزَّع على حسابات مخزون مستودعاته بنسبة كمياته. في الجرد الدوري لا قيد: الأثر في بضاعة آخر المدة.
    /// </summary>
    public static async Task<PostingResult?> PostRevaluationAsync(ErpDbContext db, IAccountingPostingService posting,
        IEnumerable<(Guid ItemId, decimal Amount)> changes, string description, Guid? sourceId, CancellationToken ct,
        (Guid WarehouseId, decimal Quantity)? excluded = null)
    {
        var accounts = await PostingAccounts.SettingsAsync(db, ct);
        if (accounts.IsPeriodic(SaudiTime.Today)) return null;
        return await PostAsync(db, posting, await ByAccountAsync(db, accounts, changes, ct, excluded), description, sourceId, ct);
    }

    /// <summary>إعادة تقييم على حساب مخزون بعينه (أثر مستند على حساب مستودعه أو حسابه اليدوي).</summary>
    public static async Task<PostingResult?> PostRevaluationAsync(ErpDbContext db, IAccountingPostingService posting, decimal amount,
        string inventoryAccount, string description, Guid? sourceId, CancellationToken ct)
    {
        if (amount == 0 || (await PostingAccounts.SettingsAsync(db, ct)).IsPeriodic(SaudiTime.Today)) return null;
        return await PostAsync(db, posting, new() { [inventoryAccount] = amount }, description, sourceId, ct);
    }

    private static async Task<PostingResult?> PostAsync(ErpDbContext db, IAccountingPostingService posting, Dictionary<string, decimal> byAccount,
        string description, Guid? sourceId, CancellationToken ct)
    {
        var changed = byAccount.Where(a => a.Value != 0).ToList();
        if (changed.Count == 0) return null;
        var configured = await db.Set<CostingPolicy>().AsNoTracking().Select(p => p.StandardCostVarianceAccountCode).FirstOrDefaultAsync(ct);
        var counter = string.IsNullOrWhiteSpace(configured) ? DefaultAccounts.InventoryAdjustment : configured.Trim();
        await DefaultAccounts.EnsureAsync(db, ct, changed.Select(a => a.Key).Append(counter).ToArray());

        var lines = changed.Select(a => a.Value > 0 ? new PostingLine(a.Key, a.Value, 0, description) : new PostingLine(a.Key, 0, -a.Value, description)).ToList();
        var total = changed.Sum(a => a.Value);
        if (total > 0) lines.Add(new(counter, 0, total, description));
        else if (total < 0) lines.Insert(0, new(counter, -total, 0, description));
        return await posting.PostAsync(new GenericPostingRequest
        {
            Date = SaudiTime.Today, Description = description, SourceType = RevaluationSource, SourceId = sourceId, Lines = lines,
        }, ct);
    }
}
