using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.EInvoicing.Application;
using Erp.Modules.EInvoicing.Contracts;
using Erp.Modules.EInvoicing.Endpoints;
using Erp.Modules.EInvoicing.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.EInvoicing;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class EInvoicingModule
{
    public const string Key = "einvoicing";

    public static IServiceCollection AddEInvoicingModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<EInvoicingDbContext>(Key, EInvoicingDbContext.SchemaName, order: 110);
        services.AddScoped<IEInvoicingService, EInvoicingService>();
        services.AddSingleton<IZatcaGateway, NotConfiguredZatcaGateway>();
        services.AddScoped<IModuleSeeder, EInvoicingSeeder>();
        services.AddHttpClient(ZatcaEndpoints.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        return services;
    }

    public static IEndpointRouteBuilder MapEInvoicingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ZatcaEndpoints.Map(endpoints);
        return endpoints;
    }
}
