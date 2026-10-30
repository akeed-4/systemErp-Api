using ERP.Service.Services.Shared;
using Microsoft.Extensions.Logging;

namespace ERP.Service.Data;

/// <summary>
/// يجهّز القاعدة المستقلة لمنشأة جديدة: ينشئها ويطبّق عليها المهاجرات القائمة نفسها، ثم ينسخ إليها سجلات دليلها
/// (المنشأة، المالك، الاشتراك...) ويزرع بياناتها الافتتاحية، ثم يعلّمها جاهزة. كل خطوة قابلة للإعادة بلا تكرار،
/// فالتجهيز المتعثّر يُعاد من لوحة المنصة. لا يمسّ منشأة قائمة على المشتركة ولا ينقل بياناتها.
/// </summary>
public sealed class TenantDatabaseProvisioner
{
    private const int MaxErrorLength = 1000;

    private readonly ITenantConnectionResolver _resolver;
    private readonly ILogger<TenantDatabaseProvisioner> _log;

    public TenantDatabaseProvisioner(ITenantConnectionResolver resolver, ILogger<TenantDatabaseProvisioner> log)
    {
        _resolver = resolver;
        _log = log;
    }

    /// <summary>سجل الربط لمنشأة جديدة مستقلة (يُضاف في معاملة إنشائها، فلا توجد لحظة تكون فيها المنشأة بلا ربط).</summary>
    public TenantDatabase NewBinding(Guid tenantId)
    {
        var (name, connection) = _resolver.DedicatedFor(tenantId);
        return new TenantDatabase
        {
            TenantId = tenantId, Mode = TenantDatabaseMode.Dedicated, Status = TenantDatabaseStatus.Provisioning,
            DatabaseName = name, ConnectionStringEncrypted = _resolver.Protect(connection),
        };
    }

    public async Task EnsureReadyAsync(Guid tenantId, CancellationToken ct = default)
    {
        _resolver.Invalidate(tenantId);
        await using var catalog = TenantDirectoryMirror.Open(_resolver, _resolver.CatalogConnectionString, tenantId);
        var binding = await catalog.Set<TenantDatabase>().FirstOrDefaultAsync(d => d.TenantId == tenantId, ct)
            ?? throw new ValidationFailedException(Messages.TenantDatabaseIsShared);
        if (binding.Status == TenantDatabaseStatus.Ready) return;

        try
        {
            var (_, connection) = _resolver.DedicatedFor(tenantId);
            await using (var dedicated = TenantDirectoryMirror.Open(_resolver, connection, tenantId))
            {
                await dedicated.Database.MigrateAsync(ct); // ينشئ القاعدة إن لم توجد ويطبّق المهاجرات القائمة
                await CopyDirectoryAsync(catalog, dedicated, ct);
                await dedicated.SaveChangesAsync(ct);
            }

            var tenant = new TenantContext();
            tenant.SetTenant(tenantId);
            await using (var seed = new ErpDbContext(_resolver.OptionsFor(connection), tenant))
            {
                if (!await seed.Set<Account>().AnyAsync(ct))
                {
                    var currency = await seed.Set<Tenant>().Select(t => t.Currency).FirstAsync(ct);
                    await new TenantProvisioningService(seed).SeedAsync(tenantId, currency, ct);
                }
            }

            binding.Status = TenantDatabaseStatus.Ready;
            binding.ProvisionedAt = DateTime.UtcNow;
            binding.LastError = null;
            await catalog.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "تعذّر تجهيز القاعدة المستقلة {Database} للمنشأة {TenantId}.", binding.DatabaseName, tenantId);
            binding.Status = TenantDatabaseStatus.Failed;
            binding.LastError = ex.Message.Length > MaxErrorLength ? ex.Message[..MaxErrorLength] : ex.Message;
            await catalog.SaveChangesAsync(CancellationToken.None);
            throw new ConflictException(Messages.TenantDatabaseProvisioningFailed);
        }
        finally
        {
            _resolver.Invalidate(tenantId);
        }
    }

    /// <summary>سجلات دليل المنشأة من المشتركة إلى المستقلة بمعرّفاتها (المنشأة كاملةً بإعداداتها المملوكة عند إضافتها أول مرة).</summary>
    private static async Task CopyDirectoryAsync(ErpDbContext catalog, ErpDbContext dedicated, CancellationToken ct)
    {
        await CopyAsync<Tenant>(catalog, dedicated, ct);
        await CopyAsync<User>(catalog, dedicated, ct);
        await CopyAsync<Subscription>(catalog, dedicated, ct);
        await CopyAsync<UserInvite>(catalog, dedicated, ct);
        await CopyAsync<OnlinePayment>(catalog, dedicated, ct);
        await CopyAsync<PaymentGatewaySettings>(catalog, dedicated, ct);
        await CopyAsync<TenantWhatsAppIntegration>(catalog, dedicated, ct);
    }

    private static async Task CopyAsync<T>(ErpDbContext catalog, ErpDbContext dedicated, CancellationToken ct) where T : BaseEntity
    {
        // سياق المشتركة مضبوط على المنشأة نفسها، فمرشّحها يحصر القراءة في سجلاتها
        foreach (var row in await catalog.Set<T>().AsNoTracking().ToListAsync(ct))
            await TenantDirectoryMirror.UpsertAsync(dedicated, typeof(T), row.Id, row, ct);
    }
}
