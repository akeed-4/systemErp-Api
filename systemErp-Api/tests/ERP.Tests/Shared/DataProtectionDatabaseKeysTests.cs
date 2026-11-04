using ERP.Core.Contracts.Shared;
using ERP.Service;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// مفاتيح تشفير الأسرار في جدول DataProtectionKeys بالقاعدة المشتركة: سرّ شفّره خادم يفكّه خادم آخر (أو الخادم نفسه
/// بعد إعادة النشر) بلا أي ملفات على السرفر، ومفاتيح المجلد القديم تُنقل إلى الجدول فتبقى أسرارها مقروءة.
/// </summary>
[Collection("api")]
public class DataProtectionDatabaseKeysTests : TestBase
{
    // تشغيل الخادم يُنشئ قاعدة الاختبار ويرحّلها (ومعها جدول المفاتيح) قبل أي خادم إضافي
    public DataProtectionDatabaseKeysTests(ErpFactory factory) : base(factory) => _ = factory.Services;

    /// <summary>تطبيق جديد بالكامل (حاوية خدمات جديدة، بلا ملفات مفاتيح) على قاعدة الاختبار نفسها.</summary>
    private async Task<T> WithServerAsync<T>(Dictionary<string, string?> settings, Func<IServiceProvider, Task<T>> use)
    {
        settings["ConnectionStrings:DefaultConnection"] = Factory.ConnectionString;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddErpServices(config);
        await using var provider = services.BuildServiceProvider();
        return await use(provider);
    }

    private static T Protector<T>(IServiceProvider provider, Func<ISecretProtector, T> use)
    {
        using var scope = provider.CreateScope();
        return use(scope.ServiceProvider.GetRequiredService<ISecretProtector>());
    }

    private async Task<List<string?>> StoredKeyNamesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DataProtectionKeysDbContext>();
        return await db.DataProtectionKeys.AsNoTracking().Select(k => k.FriendlyName).ToListAsync();
    }

    [Fact]
    public async Task Keys_live_in_the_shared_database_so_another_server_can_decrypt()
    {
        string cipher;
        using (var scope = Factory.Services.CreateScope())
            cipher = scope.ServiceProvider.GetRequiredService<ISecretProtector>().Protect("paymob-secret");

        Assert.NotEmpty(await StoredKeyNamesAsync());
        // خادم آخر (أو بعد إعادة نشر بلا مجلد مفاتيح): المفتاح يُقرأ من الجدول
        var plain = await WithServerAsync(new(), p => Task.FromResult(Protector(p, s => s.TryUnprotect(cipher))));
        Assert.Equal("paymob-secret", plain);
    }

    [Fact]
    public async Task Keys_from_the_old_folder_are_imported_once_and_their_secrets_stay_readable()
    {
        var keysPath = Path.Combine(Path.GetTempPath(), $"erp-legacy-keys-{Guid.NewGuid():N}");
        try
        {
            // الوضع القديم: مفاتيح في مجلد على السرفر
            var cipher = await WithServerAsync(new() { ["DataProtection:Store"] = "FileSystem", ["DataProtection:KeysPath"] = keysPath },
                p => Task.FromResult(Protector(p, s => s.Protect("old-paymob-secret"))));
            var legacyNames = Directory.GetFiles(keysPath, "key-*.xml").Select(Path.GetFileNameWithoutExtension).ToList();
            Assert.NotEmpty(legacyNames);

            // أول تشغيل بعد التحويل: النقل قبل أي استخدام، ثم الفك من الجدول
            var (imported, again, plain) = await WithServerAsync(new() { ["DataProtection:KeysPath"] = keysPath }, async p =>
            {
                var config = p.GetRequiredService<IConfiguration>();
                var first = await ServiceCollectionExtensions.ImportLegacyDataProtectionKeysAsync(p, config);
                var second = await ServiceCollectionExtensions.ImportLegacyDataProtectionKeysAsync(p, config);
                return (first, second, Protector(p, s => s.TryUnprotect(cipher)));
            });

            Assert.Equal(legacyNames.Count, imported);
            Assert.Equal(0, again); // لا تكرار
            Assert.Equal("old-paymob-secret", plain);
            Assert.Subset((await StoredKeyNamesAsync()).ToHashSet(), legacyNames.Cast<string?>().ToHashSet());

            // وبعد حذف المجلد (نقل السرفر) يبقى السرّ مقروءاً من الجدول وحده
            Directory.Delete(keysPath, recursive: true);
            Assert.Equal("old-paymob-secret",
                await WithServerAsync(new(), p => Task.FromResult(Protector(p, s => s.TryUnprotect(cipher)))));
        }
        finally
        {
            if (Directory.Exists(keysPath)) Directory.Delete(keysPath, recursive: true);
        }
    }
}
