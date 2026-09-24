using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Service;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// يسجّل قاعدة البيانات وكل الخدمات: أي صنف في ERP.Service ينفّذ واجهة من ERP.Core.Contracts.* يُسجَّل تلقائياً،
    /// فلا تُنسى خدمة جديدة عند إضافتها. <paramref name="configureDb"/> تستبدل SQL Server (تستخدمها الاختبارات).
    /// </summary>
    public static IServiceCollection AddErpServices(this IServiceCollection services, IConfiguration config,
        Action<DbContextOptionsBuilder>? configureDb = null)
    {
        services.AddHttpContextAccessor();

        services.AddDbContext<ErpDbContext>(options =>
        {
            if (configureDb != null) configureDb(options);
            else
                options.UseSqlServer(config.GetConnectionString("DefaultConnection"),
                    sql => sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName));
        });

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        var contractsRoot = "ERP.Core.Contracts";
        var implementations = typeof(ServiceCollectionExtensions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace?.StartsWith("ERP.Service.Services") == true);
        foreach (var impl in implementations)
            foreach (var iface in impl.GetInterfaces().Where(i => !i.IsGenericType && i.Namespace?.StartsWith(contractsRoot) == true))
            {
                if (iface == typeof(ITenantContext) || iface == typeof(ICurrentUser)) continue;
                services.AddScoped(iface, impl);
            }
        return services;
    }
}
