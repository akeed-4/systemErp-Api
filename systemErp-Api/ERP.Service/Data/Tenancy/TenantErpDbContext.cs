using System.Data.Common;
using ERP.Core.Contracts.Shared;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Service.Data;

/// <summary>
/// سياق الطلب الموجَّه بالمنشأة: النموذج نفسه لـ <see cref="ErpDbContext"/> (وكل الخدمات تحقنه بهذا الاسم)، لكن كل اتصال يُفتح
/// على قاعدة المنشأة الحالية (<see cref="TenantConnectionInterceptor"/>): المشتركة، أو المستقلة لمنشأة مستقلة.
/// مرشّح TenantId وختمه يبقيان كما هما في الحالتين. ما هو على مستوى المنصة أو عابر للمنشآت يُقرأ من <see cref="Catalog"/>.
/// المهاجرات مسجّلة على ErpDbContext، فلا تُطبَّق من هذا السياق (يُطبَّقها Catalog أو مجهِّز القاعدة المستقلة).
/// </summary>
public sealed class TenantErpDbContext : ErpDbContext
{
    private readonly ITenantContext _tenant;
    private readonly TenantCatalogOptions _catalogOptions;
    private ErpDbContext? _catalog;

    public TenantErpDbContext(DbContextOptions<TenantErpDbContext> options, ITenantContext tenant, TenantCatalogOptions catalogOptions)
        : base(options, tenant)
    {
        _tenant = tenant;
        _catalogOptions = catalogOptions;
    }

    /// <summary>منشأة القاعدة المستقلة التي فُتح عليها آخر اتصال؛ null = القاعدة المشتركة.</summary>
    public Guid? ConnectedDedicatedTenantId { get; internal set; }
    internal string? ConnectedConnectionString { get; set; }

    public override ErpDbContext Catalog => _catalog ??= new ErpDbContext(_catalogOptions.Options, _tenant);

    public override void Dispose()
    {
        _catalog?.Dispose();
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_catalog != null) await _catalog.DisposeAsync();
        await base.DisposeAsync();
    }
}

/// <summary>خيارات سياق القاعدة المشتركة في نطاق الطلب: باعتراضات التدقيق ونسخ الدليل نفسها، بلا موجِّه.</summary>
public sealed class TenantCatalogOptions
{
    public TenantCatalogOptions(ITenantConnectionResolver resolver, AuditTrailInterceptor audit, TenantDirectoryMirror mirror)
        => Options = resolver.OptionsFor(resolver.CatalogConnectionString, audit, mirror);

    public DbContextOptions<ErpDbContext> Options { get; }
}

/// <summary>
/// يوجّه اتصال <see cref="TenantErpDbContext"/> عند فتحه إلى قاعدة المنشأة الحالية. يُقيَّم عند كل فتح (لا عند إنشاء السياق)
/// لأن المنشأة تُعرف أحياناً أثناء الطلب (الدخول، رابط الدعوة، إشعار الدفع)؛ والاتصال المفتوح في معاملة يبقى على قاعدته حتى تنتهي.
/// </summary>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenant;
    private readonly ITenantConnectionResolver _resolver;

    public TenantConnectionInterceptor(ITenantContext tenant, ITenantConnectionResolver resolver)
    {
        _tenant = tenant;
        _resolver = resolver;
    }

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Route(connection, eventData.Context, _resolver.Resolve(_tenant.TenantId));
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Route(connection, eventData.Context, await _resolver.ResolveAsync(_tenant.TenantId, cancellationToken));
        return result;
    }

    private static void Route(DbConnection connection, DbContext? context, TenantDatabaseTarget target)
    {
        if (context is not TenantErpDbContext db) return;
        if (db.ConnectedConnectionString != target.ConnectionString)
        {
            connection.ConnectionString = target.ConnectionString;
            db.ConnectedConnectionString = target.ConnectionString;
        }
        db.ConnectedDedicatedTenantId = target.DedicatedTenantId;
    }
}
