using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Suppliers.Application;
using Erp.Modules.Suppliers.Contracts;
using Erp.Modules.Suppliers.Endpoints;
using Erp.Modules.Suppliers.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Suppliers;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class SuppliersModule
{
    public const string Key = "suppliers";

    public static IServiceCollection AddSuppliersModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<SuppliersDbContext>(Key, SuppliersDbContext.SchemaName, order: 80);
        services.AddScoped<SupplierService>();
        services.AddScoped<ISupplierDirectory, SupplierDirectory>();
        return services;
    }

    public static IEndpointRouteBuilder MapSuppliersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        SupplierEndpoints.Map(endpoints);
        return endpoints;
    }
}
