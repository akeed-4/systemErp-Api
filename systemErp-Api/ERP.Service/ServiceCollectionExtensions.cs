using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.DataProtection;
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

        services.AddScoped<AuditTrailInterceptor>();
        // قاعدة كل منشأة: المشتركة (DefaultConnection، وهي أيضاً دليل المنصة) أو المستقلة. الخدمات تحقن ErpDbContext
        // كما هي فتحصل على السياق الموجَّه بالمنشأة؛ والمشتركة صراحةً عبر ErpDbContext.Catalog.
        services.AddSingleton<ITenantConnectionResolver>(provider => new TenantConnectionResolver(
            () => config.GetConnectionString("DefaultConnection") ?? string.Empty, configureDb,
            provider.GetRequiredService<IDataProtectionProvider>()));
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<TenantDirectoryMirror>();
        services.AddScoped<TenantCatalogOptions>();
        services.AddScoped<TenantDatabaseProvisioner>();
        services.AddDbContext<TenantErpDbContext>((provider, options) =>
        {
            if (configureDb != null) configureDb(options);
            else
                options.UseSqlServer(config.GetConnectionString("DefaultConnection"),
                    sql => sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName));
            options.AddInterceptors(provider.GetRequiredService<AuditTrailInterceptor>(),
                provider.GetRequiredService<TenantDirectoryMirror>(), provider.GetRequiredService<TenantConnectionInterceptor>());
        });
        services.AddScoped<ErpDbContext>(provider => provider.GetRequiredService<TenantErpDbContext>());

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<PaymobOptions>(config.GetSection(PaymobOptions.Section));
        services.Configure<PlatformOptions>(config.GetSection(PlatformOptions.Section));
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        services.AddHttpClient("paymob", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(ERP.Service.Services.Accounting.ZatcaApiClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(45));
        // مفاتيح تشفير أسرار بوابات الدفع: تُحفظ في مسار دائم (DataProtection:KeysPath) لتبقى صالحة بعد إعادة النشر،
        // وباسم تطبيق ثابت لا يتغيّر بتغيّر مسار التنصيب. بلا المسار تبقى في ملف المستخدم على السرفر نفسه.
        var dataProtection = services.AddDataProtection().SetApplicationName("ERP");
        var keysPath = config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath)) dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
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
                // العقود المشتركة لمستندات التجارة تُحقن بأنواعها (ISalesInvoiceService...) لا بالعقد العام
                if (iface == typeof(ERP.Core.Contracts.Accounting.ITradeDocumentService) || iface == typeof(ERP.Core.Contracts.Accounting.ITradeReturnService)) continue;
                services.AddScoped(iface, impl);
            }
        // محرك الفواتير المشترك بين خدمات جداول الفواتير (التجارة العامة والمعارض)
        services.AddScoped<ERP.Service.Services.Accounting.TradeInvoiceEngine>();
        return services;
    }
}
