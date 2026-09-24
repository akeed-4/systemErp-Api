using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Settings.Application;
using Erp.Modules.Settings.Contracts;
using Erp.Modules.Settings.Endpoints;
using Erp.Modules.Settings.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Settings;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class SettingsModule
{
    public const string Key = "settings";

    public static IServiceCollection AddSettingsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<SettingsDbContext>(Key, SettingsDbContext.SchemaName, order: 40);
        services.AddScoped<INumberSequenceService, NumberSequenceService>();
        services.AddScoped<ICurrencyLookup, CurrencyLookup>();
        services.AddScoped<IModuleSeeder, SettingsSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        SettingsEndpoints.Map(endpoints);
        return endpoints;
    }
}
