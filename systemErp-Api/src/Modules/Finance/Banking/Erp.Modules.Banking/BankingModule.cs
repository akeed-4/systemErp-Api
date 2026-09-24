using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Banking.Application;
using Erp.Modules.Banking.Contracts;
using Erp.Modules.Banking.Endpoints;
using Erp.Modules.Banking.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Banking;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class BankingModule
{
    public const string Key = "banking";

    public static IServiceCollection AddBankingModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<BankingDbContext>(Key, BankingDbContext.SchemaName, order: 60);
        services.AddScoped<IBankDirectory, BankDirectory>();
        services.AddScoped<BankAccountService>();
        services.AddScoped<IModuleSeeder, BankingSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapBankingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        BankingEndpoints.Map(endpoints);
        return endpoints;
    }
}
