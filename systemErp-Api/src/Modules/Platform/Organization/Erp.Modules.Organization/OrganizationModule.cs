using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Organization.Application;
using Erp.Modules.Organization.Contracts;
using Erp.Modules.Organization.Endpoints;
using Erp.Modules.Organization.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Organization;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class OrganizationModule
{
    public const string Key = "org";

    public static IServiceCollection AddOrganizationModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<OrganizationDbContext>(Key, OrganizationDbContext.SchemaName, order: 10);
        services.AddScoped<IModuleSeeder, OrganizationSeeder>();
        services.AddScoped<ICompanyProfileReader, CompanyProfileReader>();
        services.AddScoped<IBranchDirectory, BranchDirectory>();
        return services;
    }

    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        OrganizationEndpoints.Map(endpoints);
        return endpoints;
    }
}
