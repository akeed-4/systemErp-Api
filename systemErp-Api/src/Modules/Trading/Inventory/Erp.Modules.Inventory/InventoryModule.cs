using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Inventory.Application;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Inventory.Endpoints;
using Erp.Modules.Inventory.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Inventory;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class InventoryModule
{
    public const string Key = "inventory";

    public static IServiceCollection AddInventoryModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<InventoryDbContext>(Key, InventoryDbContext.SchemaName, order: 100);
        services.AddScoped<IInventoryService, InventoryEngine>();
        services.AddScoped<IProductCatalog, ProductCatalog>();
        services.AddScoped<IWarehouseDirectory, WarehouseDirectory>();
        services.AddScoped<ProductService>();
        services.AddScoped<StockOperations>();
        services.AddScoped<IModuleSeeder, InventorySeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ProductEndpoints.Map(endpoints);
        InventorySetupEndpoints.Map(endpoints);
        StockEndpoints.Map(endpoints);
        return endpoints;
    }
}
