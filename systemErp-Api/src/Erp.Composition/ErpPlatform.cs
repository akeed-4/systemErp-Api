using Erp.BuildingBlocks.Infrastructure;
using Erp.Catalog;
using Erp.Modules.Accounting;
using Erp.Modules.Banking;
using Erp.Modules.Customers;
using Erp.Modules.EInvoicing;
using Erp.Modules.Inventory;
using Erp.Modules.Payments;
using Erp.Modules.Suppliers;
using Erp.Modules.Identity;
using Erp.Modules.Organization;
using Erp.Modules.Permissions;
using Erp.Modules.Settings;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Composition;

/// <summary>
/// The single list of building blocks and modules, shared by Erp.Api, Erp.Migrator and the tests,
/// so every host migrates and serves exactly the same set of module databases.
/// </summary>
public static class ErpPlatform
{
    public static IServiceCollection AddErpPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddErpInfrastructure(configuration);
        services.AddErpCatalog();

        // Tenant-database modules, in migration order (§17.2):
        // platform(0) → org(10) → identity(20) → authz(30) → settings(40) → accounting(50)
        // → banking(60) → customers(70) → suppliers(80) → payments(90) → inventory(100) → einvoicing(110).
        services.AddOrganizationModule();
        services.AddIdentityModule(configuration);
        services.AddPermissionsModule();
        services.AddSettingsModule();
        services.AddAccountingModule();
        services.AddBankingModule();
        services.AddCustomersModule();
        services.AddSuppliersModule();
        services.AddPaymentsModule();
        services.AddInventoryModule();
        services.AddEInvoicingModule();
        return services;
    }

    public static IEndpointRouteBuilder MapErpModules(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOrganizationEndpoints();
        endpoints.MapIdentityEndpoints();
        endpoints.MapPermissionsEndpoints();
        endpoints.MapSettingsEndpoints();
        endpoints.MapAccountingEndpoints();
        endpoints.MapBankingEndpoints();
        endpoints.MapCustomersEndpoints();
        endpoints.MapSuppliersEndpoints();
        endpoints.MapPaymentsEndpoints();
        endpoints.MapInventoryEndpoints();
        endpoints.MapEInvoicingEndpoints();
        return endpoints;
    }
}
