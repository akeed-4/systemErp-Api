using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.Catalog.Contracts;
using Erp.Catalog.Persistence;
using Erp.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Erp.Catalog;

public static class CatalogServiceCollectionExtensions
{
    public static IServiceCollection AddErpCatalog(this IServiceCollection services)
    {
        services.AddDbContextFactory<CatalogDbContext>((sp, options) =>
            options.UseSqlServer(
                sp.GetRequiredService<IOptions<TenancyOptions>>().Value.CatalogConnectionString,
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", CatalogDbContext.Schema)));

        services.AddSingleton<ITenantDirectory, TenantDirectory>();
        services.AddSingleton<ITenantLoginIndex, TenantLoginIndex>();
        services.AddSingleton<ISubscriptionCatalog, SubscriptionCatalog>();
        services.AddSingleton<IGlobalReferenceDataSource, GlobalReferenceDataSource>();
        services.AddSingleton<ITenantProvisioningService, TenantProvisioningService>();
        services.AddSingleton<IDatabaseMigrationOrchestrator, DatabaseMigrationOrchestrator>();
        return services;
    }
}
