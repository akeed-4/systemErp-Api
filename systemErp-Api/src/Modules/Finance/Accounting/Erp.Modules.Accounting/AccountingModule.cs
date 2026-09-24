using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Accounting.Application;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Accounting.Endpoints;
using Erp.Modules.Accounting.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Accounting;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class AccountingModule
{
    public const string Key = "accounting";

    public static IServiceCollection AddAccountingModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<AccountingDbContext>(Key, AccountingDbContext.SchemaName, order: 50);

        services.AddScoped<PostingEngine>();
        services.AddScoped<AccountingPostingService>();
        services.AddScoped<IAccountingPostingService>(sp => sp.GetRequiredService<AccountingPostingService>());
        services.AddScoped<IAccountLookup, AccountLookup>();
        services.AddScoped<IAccountProvisioningService, AccountProvisioningService>();
        services.AddScoped<IAccountBalanceQueries, AccountBalanceQueries>();
        services.AddScoped<ICostCenterLookup, CostCenterLookup>();
        services.AddScoped<IAccountStatementService, AccountStatementService>();
        services.AddScoped<AccountingQueries>();
        services.AddScoped<IModuleSeeder, AccountingSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapAccountingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        AccountEndpoints.Map(endpoints);
        JournalEntryEndpoints.Map(endpoints);
        AccountingSetupEndpoints.Map(endpoints);
        AccountingReportEndpoints.Map(endpoints);
        return endpoints;
    }
}
