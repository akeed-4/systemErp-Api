using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.Caching.Memory;

namespace Erp.BuildingBlocks.Infrastructure.Tenancy;

/// <summary>IMemoryCache wrapper whose keys always carry the current TenantId ("t:{tenantId}:{key}").</summary>
public interface ITenantCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken);

    void Remove(string key);
}

internal sealed class TenantCache(IMemoryCache cache, ITenantContext tenant) : ITenantCache
{
    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var fullKey = BuildKey(key);
        if (cache.TryGetValue(fullKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        cache.Set(fullKey, value, ttl);
        return value;
    }

    public void Remove(string key) => cache.Remove(BuildKey(key));

    private string BuildKey(string key) => $"t:{tenant.TenantId:N}:{key}";
}
