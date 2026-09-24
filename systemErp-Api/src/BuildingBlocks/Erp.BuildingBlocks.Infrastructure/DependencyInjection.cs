using Erp.BuildingBlocks.Infrastructure.Schema;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Infrastructure.Outbox;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Security;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.SharedKernel.Messaging;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    public const string PlatformModuleKey = "platform";

    public static IServiceCollection AddErpInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TenancyOptions>()
            .Bind(configuration.GetSection(TenancyOptions.SectionName))
            .PostConfigure(o =>
            {
                if (string.IsNullOrWhiteSpace(o.CatalogConnectionString))
                {
                    o.CatalogConnectionString = configuration.GetConnectionString("Catalog") ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(o.SharedConnectionString))
                {
                    o.SharedConnectionString = configuration.GetConnectionString("Shared") ?? string.Empty;
                }
            });
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        // Api and Migrator must share the key ring, otherwise the migrator cannot read connection strings the API wrote.
        var dataProtection = services.AddDataProtection().SetApplicationName("SystemErp");
        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddSingleton<IConnectionStringProtector, DataProtectionConnectionStringProtector>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddSingleton<ITenantConnectionFactory, TenantConnectionFactory>();
        services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
        services.AddSingleton<ISchemaVersionProvider, SchemaVersionProvider>();
        services.AddSingleton<TenantGuardInterceptor>();
        services.AddSingleton<TenantDatabaseMigrator>();
        services.AddSingleton<OutboxDispatcher>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<ITenantDbConnection, TenantDbConnection>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IPlatformScope, PlatformScope>();
        services.AddScoped<ITenantCache, TenantCache>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.AddScoped(sp => new ModuleDbContextDependencies(
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<IPlatformScope>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<IUnitOfWork>()));

        services.AddModuleDbContext<PlatformDbContext>(PlatformModuleKey, PlatformDbContext.SchemaName, order: 0);
        services.AddScoped<IOutbox, OutboxWriter>();
        return services;
    }

    /// <summary>API host only: the hosted service that drains every tenant database's outbox.</summary>
    public static IServiceCollection AddErpOutboxDispatcher(this IServiceCollection services)
    {
        services.AddHostedService<OutboxBackgroundService>();
        return services;
    }
}
