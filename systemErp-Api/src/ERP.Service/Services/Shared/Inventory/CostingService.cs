using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CostingService : ICostingService
{
    private readonly ErpDbContext _db;
    public CostingService(ErpDbContext db) => _db = db;

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

    public async Task<CostingPolicyDto> SetPolicyAsync(CreateCostingPolicyDto d, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(d.Method)) throw new ValidationFailedException("طريقة التكلفة غير صالحة.");
        if (d.NegativeInventoryPolicy is not ("prohibit" or "allow_with_last_cost"))
            throw new ValidationFailedException("سياسة المخزون السالب: prohibit | allow_with_last_cost.");
        if (!string.IsNullOrWhiteSpace(d.StandardCostVarianceAccountCode)
            && !await _db.Set<Account>().AnyAsync(a => a.Code == d.StandardCostVarianceAccountCode, ct))
            throw new ValidationFailedException("حساب فروقات التكلفة المعيارية غير موجود.");

        var policy = await _db.Set<CostingPolicy>().FirstOrDefaultAsync(ct);
        if (policy == null) { policy = new CostingPolicy(); _db.Add(policy); }
        Mapper.Apply(d, policy);
        policy.LastUpdated = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<CostingPolicyDto>(policy);
    }

    /// <summary>يعيد بناء رصيد ومتوسط تكلفة كل صنف من حركاته الفعلية بترتيبها الزمني وفق الطريقة الحالية.</summary>
    public async Task<RecalculateCostingResultDto> RecalculateAllAsync(CancellationToken ct = default)
    {
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var products = await _db.Set<Product>().ToListAsync(ct);
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
        return new RecalculateCostingResultDto { ProductsUpdated = updated, Method = policy.Method };
    }
}
