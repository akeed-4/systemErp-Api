using System.Collections.Concurrent;
using ERP.Core.Contracts.Shared;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Service.Data;

/// <summary>القاعدة التي يُفتح عليها الاتصال: المشتركة (DedicatedTenantId = null) أو المستقلة لمنشأة.</summary>
public sealed record TenantDatabaseTarget(string ConnectionString, Guid? DedicatedTenantId);

/// <summary>
/// يحدّد قاعدة كل منشأة: بلا منشأة أو منشأة مشتركة ⇒ القاعدة المشتركة؛ منشأة مستقلة جاهزة ⇒ قاعدتها؛
/// مستقلة لم تجهز ⇒ يُرفض الطلب (لا رجوع صامت للمشتركة يكتب بياناتها في غير قاعدتها).
/// </summary>
public interface ITenantConnectionResolver
{
    string CatalogConnectionString { get; }
    TenantDatabaseTarget Resolve(Guid? tenantId);
    ValueTask<TenantDatabaseTarget> ResolveAsync(Guid? tenantId, CancellationToken ct = default);
    /// <summary>نص اتصال القاعدة المستقلة الجاهزة، أو null للمنشأة المشتركة أو التي لم تجهز (لنسخ سجلات الدليل).</summary>
    ValueTask<string?> ReadyDedicatedAsync(Guid tenantId, CancellationToken ct = default);
    /// <summary>خيارات سياق على قاعدة بعينها (المشتركة أو مستقلة) بالنموذج والمهاجرات نفسها.</summary>
    DbContextOptions<ErpDbContext> OptionsFor(string connectionString, params IInterceptor[] interceptors);
    /// <summary>اسم القاعدة المستقلة للمنشأة ونص اتصالها: الخادم وبيانات الدخول نفسها للمشتركة، باسم مشتق من معرّفها.</summary>
    (string DatabaseName, string ConnectionString) DedicatedFor(Guid tenantId);
    string Protect(string connectionString);
    void Invalidate(Guid tenantId);
}

public sealed class TenantConnectionResolver : ITenantConnectionResolver
{
    private readonly Lazy<string> _catalog;
    private readonly Action<DbContextOptionsBuilder>? _configureCatalog;
    private readonly IDataProtector _protector;
    /// <summary>المشتركة والمستقلة الجاهزة فقط تُحفظ؛ قيد التجهيز يُعاد سؤاله في كل مرة حتى يجهز.</summary>
    private readonly ConcurrentDictionary<Guid, TenantDatabaseTarget> _cache = new();

    public TenantConnectionResolver(Func<string> catalogConnectionString, Action<DbContextOptionsBuilder>? configureCatalog, IDataProtectionProvider protection)
    {
        _catalog = new Lazy<string>(catalogConnectionString);
        _configureCatalog = configureCatalog;
        _protector = protection.CreateProtector("ERP.TenantDatabase.ConnectionString.v1");
    }

    public string CatalogConnectionString => _catalog.Value;
    private TenantDatabaseTarget CatalogTarget => new(CatalogConnectionString, null);

    public TenantDatabaseTarget Resolve(Guid? tenantId)
    {
        if (tenantId is not { } id || id == Guid.Empty) return CatalogTarget;
        if (_cache.TryGetValue(id, out var cached)) return cached;
        using var db = CatalogLookup();
        return Remember(id, db.Set<TenantDatabase>().AsNoTracking().FirstOrDefault(d => d.TenantId == id));
    }

    public async ValueTask<TenantDatabaseTarget> ResolveAsync(Guid? tenantId, CancellationToken ct = default)
    {
        if (tenantId is not { } id || id == Guid.Empty) return CatalogTarget;
        if (_cache.TryGetValue(id, out var cached)) return cached;
        await using var db = CatalogLookup();
        return Remember(id, await db.Set<TenantDatabase>().AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == id, ct));
    }

    public async ValueTask<string?> ReadyDedicatedAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached)) return cached.DedicatedTenantId == null ? null : cached.ConnectionString;
        await using var db = CatalogLookup();
        var row = await db.Set<TenantDatabase>().AsNoTracking().FirstOrDefaultAsync(d => d.TenantId == tenantId, ct);
        if (row is { Status: not TenantDatabaseStatus.Ready }) return null;
        var target = Remember(tenantId, row);
        return target.DedicatedTenantId == null ? null : target.ConnectionString;
    }

    private TenantDatabaseTarget Remember(Guid tenantId, TenantDatabase? row)
    {
        if (row == null || row.Mode == TenantDatabaseMode.Shared) return _cache[tenantId] = CatalogTarget;
        if (row.Status != TenantDatabaseStatus.Ready) throw new ForbiddenException(Messages.TenantDatabaseNotReady);
        return _cache[tenantId] = new TenantDatabaseTarget(_protector.Unprotect(row.ConnectionStringEncrypted), tenantId);
    }

    /// <summary>قراءة الربط من المشتركة مباشرة: سياق بلا موجِّه ولا اعتراضات (لا تكرار ولا تدقيق).</summary>
    private ErpDbContext CatalogLookup() => new(OptionsFor(CatalogConnectionString), new TenantContext());

    public DbContextOptions<ErpDbContext> OptionsFor(string connectionString, params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ErpDbContext>();
        if (_configureCatalog != null && connectionString == CatalogConnectionString) _configureCatalog(builder);
        else builder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName));
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    public (string DatabaseName, string ConnectionString) DedicatedFor(Guid tenantId)
    {
        var builder = new SqlConnectionStringBuilder(CatalogConnectionString);
        var prefix = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "ErpDb" : builder.InitialCatalog;
        // المعرّف بصيغة N (أرقام وحروف ست عشرية) فلا يحمل الاسم أي محرف يُفسَّر في SQL
        builder.InitialCatalog = $"{prefix}_t{tenantId:N}";
        return (builder.InitialCatalog, builder.ConnectionString);
    }

    public string Protect(string connectionString) => _protector.Protect(connectionString);

    public void Invalidate(Guid tenantId) => _cache.TryRemove(tenantId, out _);
}
