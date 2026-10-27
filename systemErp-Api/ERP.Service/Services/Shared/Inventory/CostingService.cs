using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CostingService : ICostingService
{
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;

    public CostingService(ErpDbContext db, IAccountingPostingService posting, ITransactionRunner tx)
    {
        _db = db; _posting = posting; _tx = tx;
    }

    public async Task<CostingPolicyDto> GetPolicyAsync(CancellationToken ct = default)
    {
        var policy = await _db.Set<CostingPolicy>().FirstOrDefaultAsync(ct);
        if (policy == null)
        {
            policy = new CostingPolicy { StandardCostVarianceAccountCode = Accounting.DefaultAccounts.InventoryAdjustment };
            _db.Add(policy);
            await _db.SaveChangesAsync(ct);
        }
        return Mapper.Map<CostingPolicyDto>(policy);
    }

    /// <summary>تغيير طريقة التكلفة يعيد احتساب تكلفة الأصناف بالطريقة الجديدة فوراً ويرحّل فرق القيمة، فلا يفترق المخزون عن حسابه.</summary>
    public Task<CostingPolicyDto> SetPolicyAsync(CreateCostingPolicyDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var previous = await _db.Set<CostingPolicy>().AsNoTracking().Select(p => (CostingMethod?)p.Method).FirstOrDefaultAsync(token);
            var saved = await SavePolicyAsync(d, token);
            if (previous != d.Method) await RecalculateCoreAsync($"إعادة تقييم المخزون بتغيير طريقة التكلفة إلى {d.Method}", token);
            return saved;
        }, ct);

    private async Task<CostingPolicyDto> SavePolicyAsync(CreateCostingPolicyDto d, CancellationToken ct)
    {
        if (!Enum.IsDefined(d.Method)) throw new ValidationFailedException(Messages.InvalidCostingMethod);
        if (d.NegativeInventoryPolicy is not ("prohibit" or "allow_with_last_cost"))
            throw new ValidationFailedException(Messages.NegativeInventoryPolicyValues);
        if (!string.IsNullOrWhiteSpace(d.StandardCostVarianceAccountCode)
            && !await _db.Set<Account>().AnyAsync(a => a.Code == d.StandardCostVarianceAccountCode, ct))
            throw new ValidationFailedException(Messages.StandardCostVarianceAccountNotFound);

        var policy = await _db.Set<CostingPolicy>().FirstOrDefaultAsync(ct);
        if (policy == null) { policy = new CostingPolicy(); _db.Add(policy); }
        Mapper.Apply(d, policy);
        policy.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<CostingPolicyDto>(policy);
    }

    /// <summary>
    /// يعيد بناء رصيد ومتوسط تكلفة كل صنف من حركاته الفعلية بترتيبها الزمني وفق الطريقة الحالية،
    /// ويرحّل فرق قيمة المخزون بقيد إعادة تقييم (لا تُغيَّر تكلفة مبيعات مرحّلة سابقاً).
    /// </summary>
    public Task<RecalculateCostingResultDto> RecalculateAllAsync(CancellationToken ct = default)
        => _tx.RunAsync(token => RecalculateCoreAsync("إعادة تقييم المخزون بإعادة احتساب التكلفة", token), ct);

    private async Task<RecalculateCostingResultDto> RecalculateCoreAsync(string description, CancellationToken ct)
    {
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var products = await _db.Set<Product>().ToListAsync(ct);
        var before = InventoryValuation.Snapshot(products);
        var movements = (await _db.Set<StockMovement>().OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToListAsync(ct))
            .GroupBy(m => m.ItemId).ToDictionary(g => g.Key, g => g.ToList());

        var updated = 0;
        foreach (var p in products)
        {
            if (!movements.TryGetValue(p.Id, out var list)) continue;
            StockReplay.Apply(p, list, policy);
            updated++;
        }
        await _db.SaveChangesAsync(ct);

        var difference = InventoryValuation.Difference(products, before);
        var entry = await InventoryValuation.PostRevaluationAsync(_db, _posting,
            products.Select(p => (p.Id, InventoryValuation.Value(p) - before.GetValueOrDefault(p.Id))), description, null, ct);
        return new RecalculateCostingResultDto
        {
            ProductsUpdated = updated, Method = policy.Method, RevaluationAmount = difference, RevaluationEntryNumber = entry?.EntryNumber,
        };
    }
}
