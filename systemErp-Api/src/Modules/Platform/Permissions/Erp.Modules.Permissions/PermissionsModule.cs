using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Permissions.Application;
using Erp.Modules.Permissions.Contracts;
using Erp.Modules.Permissions.Endpoints;
using Erp.Modules.Permissions.Persistence;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Permissions;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class PermissionsModule
{
    public const string Key = "authz";

    public static IServiceCollection AddPermissionsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PermissionsDbContext>(Key, PermissionsDbContext.SchemaName, order: 30);
        services.AddScoped<PermissionEvaluator>();
        services.AddScoped<IScreenPermissionChecker>(sp => sp.GetRequiredService<PermissionEvaluator>());
        services.AddScoped<IEffectivePermissions>(sp => sp.GetRequiredService<PermissionEvaluator>());
        services.AddScoped<IModuleSeeder, PermissionsSeeder>();
        services.AddScoped<IReferenceDataSeeder, ScreenReferenceSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapPermissionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        PermissionEndpoints.Map(endpoints);
        return endpoints;
    }
}
