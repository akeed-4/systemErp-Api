using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Customers.Application;
using Erp.Modules.Customers.Contracts;
using Erp.Modules.Customers.Endpoints;
using Erp.Modules.Customers.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Customers;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class CustomersModule
{
    public const string Key = "customers";

    public static IServiceCollection AddCustomersModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<CustomersDbContext>(Key, CustomersDbContext.SchemaName, order: 70);
        services.AddScoped<CustomerService>();
        services.AddScoped<ICustomerDirectory, CustomerDirectory>();
        return services;
    }

    public static IEndpointRouteBuilder MapCustomersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        CustomerEndpoints.Map(endpoints);
        return endpoints;
    }
}
