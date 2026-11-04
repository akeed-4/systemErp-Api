using Zomp.EFCore.WindowFunctions.SqlServer;
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
                    sql => sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName).UseWindowFunctions()); // SUM() OVER في LINQ (كشف الحساب)
            options.AddInterceptors(provider.GetRequiredService<AuditTrailInterceptor>(),
                provider.GetRequiredService<TenantDirectoryMirror>(), provider.GetRequiredService<TenantConnectionInterceptor>());
        });
        services.AddScoped<ErpDbContext>(provider => provider.GetRequiredService<TenantErpDbContext>());

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<PaymobOptions>(config.GetSection(PaymobOptions.Section));
        services.Configure<PlatformOptions>(config.GetSection(PlatformOptions.Section));
        services.Configure<EmailOptions>(config.GetSection(EmailOptions.Section));
        services.Configure<WhatsAppOptions>(config.GetSection(WhatsAppOptions.Section));
        services.AddHttpClient("paymob", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(MetaWhatsAppProvider.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient(ERP.Service.Services.Accounting.ZatcaApiClient.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(45));
        // مفاتيح تشفير أسرار بوابات الدفع وسلاسل اتصال القواعد المستقلة، باسم تطبيق ثابت لا يتغيّر بتغيّر مسار التنصيب.
        // افتراضياً تُحفظ في جدول DataProtectionKeys بالقاعدة المشتركة فتبقى بعد إعادة النشر ونقل السرفر وتتشاركها كل
        // نسخ الخادم. DataProtection:Store=FileSystem (أو غياب القاعدة) يعيدها إلى مجلد DataProtection:KeysPath.
        var dataProtection = services.AddDataProtection().SetApplicationName("ERP");
        var keysPath = config["DataProtection:KeysPath"];
        var useDatabase = !string.Equals(config["DataProtection:Store"], "FileSystem", StringComparison.OrdinalIgnoreCase)
            && (configureDb != null || !string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")));
        if (useDatabase)
        {
            // القاعدة المشتركة دائماً (لا الموجَّهة بالمنشأة)، وسلسلة الاتصال تُقرأ عند الإنشاء لا عند التسجيل
            services.AddDbContext<DataProtectionKeysDbContext>(options =>
            {
                if (configureDb != null) configureDb(options);
                else options.UseSqlServer(config.GetConnectionString("DefaultConnection"));
            });
            dataProtection.PersistKeysToDbContext<DataProtectionKeysDbContext>();
        }
        // المسار النسبي يُحسب من مجلد التطبيق (على الاستضافة المشتركة لا يُحمَّل ملف المستخدم فتضيع المفاتيح عند كل إعادة تشغيل)
        else if (!string.IsNullOrWhiteSpace(keysPath))
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(LegacyKeysFolder(keysPath)!));
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

    /// <summary>مجلد مفاتيح DataProtection:KeysPath (النسبي من مجلد التطبيق)، أو null إن لم يُضبط.</summary>
    public static string? LegacyKeysFolder(string? keysPath)
        => string.IsNullOrWhiteSpace(keysPath) ? null : Path.Combine(AppContext.BaseDirectory, keysPath);

    /// <summary>
    /// عند التخزين في القاعدة: ينقل مفاتيح مجلد DataProtection:KeysPath القديم إلى الجدول (مرة واحدة، دون تكرار)
    /// قبل أول استخدام للتشفير، فتبقى أسرار Paymob المحفوظة سابقاً قابلة للفك. يُرجع عدد المفاتيح المنقولة.
    /// </summary>
    public static async Task<int> ImportLegacyDataProtectionKeysAsync(IServiceProvider services, IConfiguration config, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var keys = scope.ServiceProvider.GetService<DataProtectionKeysDbContext>();
        if (keys == null) return 0; // التخزين في مجلد، لا شيء يُنقل
        return await keys.ImportKeysFromFolderAsync(LegacyKeysFolder(config["DataProtection:KeysPath"]), ct);
    }
}
