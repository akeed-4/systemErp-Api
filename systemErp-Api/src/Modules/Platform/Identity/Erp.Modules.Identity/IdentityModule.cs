using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Identity.Application;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Endpoints;
using Erp.Modules.Identity.Persistence;
using Erp.SharedKernel.Messaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Identity;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class IdentityModule
{
    public const string Key = "identity";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityModuleOptions>().Bind(configuration.GetSection(IdentityModuleOptions.SectionName));
        services.AddModuleDbContext<IdentityDbContext>(Key, IdentityDbContext.SchemaName, order: 20);

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IOtpSender, LoggingOtpSender>();
        services.AddScoped<TokenService>();
        services.AddScoped<TenantAuthSession>();
        services.AddScoped<AuthService>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        services.AddScoped<IModuleSeeder, IdentitySeeder>();
        services.AddScoped<IReferenceDataSeeder, RoleReferenceSeeder>();
        services.AddScoped<IOutboxMessageHandler, UserLoginIndexChangedHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        IdentityEndpoints.Map(endpoints);
        return endpoints;
    }
}
