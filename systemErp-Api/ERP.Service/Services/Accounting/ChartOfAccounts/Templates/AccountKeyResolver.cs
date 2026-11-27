using System.Collections.Concurrent;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المفتاح الوظيفي ← رقم الحساب للمنشأة الحالية، للخدمات الجديدة (الخدمات القائمة تقرأ مخازنها كما هي، فلا يزيد استعلام في مساراتها).
/// الخريطة كلها تُحمَّل باستعلامين (الإعدادات والربط بحساباته) وتُحفظ لكل منشأة دقائق معدودة، وتُبطَل عند تغيير الربط أو الإعدادات أو
/// رقم حساب مربوط وعند تطبيق القوالب. لا يقرأ القوالب أبداً.
/// </summary>
public sealed class AccountKeyResolver : IAccountKeyResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, (DateTime LoadedAt, Dictionary<string, string> Map)> Cache = new();

    private readonly ErpDbContext _db;
    public AccountKeyResolver(ErpDbContext db) => _db = db;

    public static void Invalidate(Guid tenantId) => Cache.TryRemove(tenantId, out _);

    public async Task<string?> ResolveAsync(string semanticKey, CancellationToken ct = default)
    {
        var def = AccountSemanticKeys.Of(AccountSemanticKeys.Normalize(semanticKey));
        if (def.Store == AccountSemanticKeys.ReservedStore) return def.DefaultCode;

        var tenantId = _db.CurrentTenantId;
        if (!Cache.TryGetValue(tenantId, out var entry) || DateTime.UtcNow - entry.LoadedAt > Ttl)
        {
            entry = (DateTime.UtcNow, await LoadAsync(ct));
            Cache[tenantId] = entry;
        }
        return entry.Map.GetValueOrDefault(def.Key);
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        foreach (var def in AccountSemanticKeys.All.Where(d => d.Store == AccountSemanticKeys.SettingStore))
            if (def.Role!.Effective(settings) is { } code) map[def.Key] = code;

        var bound = await (from b in _db.Set<AccountBinding>().AsNoTracking()
                           join a in _db.Set<Account>().AsNoTracking() on b.AccountId equals a.Id
                           select new { b.SemanticKey, a.Code }).ToListAsync(ct);
        foreach (var b in bound) map.TryAdd(b.SemanticKey, b.Code);
        return map;
    }
}
