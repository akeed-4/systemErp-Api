using System.Security.Cryptography;
using ERP.Core.Contracts.Shared;
using ERP.Service;
using ERP.Service.Services.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// مفاتيح تشفير الأسرار (DataProtection:KeysPath): ما شُفّر قبل إعادة تشغيل التطبيق يبقى قابلاً للفك بعدها
/// ما دام مجلد المفاتيح نفسه، والمسار النسبي يُحسب من مجلد التطبيق. لا يحتاج قاعدة بيانات.
/// </summary>
public class DataProtectionKeysTests
{
    /// <summary>تطبيق جديد بالكامل (حاوية خدمات جديدة) كما بعد إعادة تشغيل أو نشر.</summary>
    private static T WithApp<T>(string keysPath, Func<ISecretProtector, T> use)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeysPath"] = keysPath }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddErpServices(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return use(scope.ServiceProvider.GetRequiredService<ISecretProtector>());
    }

    [Fact]
    public void Secrets_stay_readable_after_the_app_is_recreated_with_the_same_keys_folder()
    {
        var keysPath = Path.Combine("App_Data", $"keys-test-{Guid.NewGuid():N}");
        var folder = Path.Combine(AppContext.BaseDirectory, keysPath);
        try
        {
            var cipher = WithApp(keysPath, s => s.Protect("paymob-secret"));

            // المسار النسبي داخل مجلد التطبيق، والمفتاح محفوظ فيه
            Assert.NotEmpty(Directory.GetFiles(folder, "key-*.xml"));
            Assert.Equal("paymob-secret", WithApp(keysPath, s => s.Unprotect(cipher)));
            Assert.Equal("paymob-secret", WithApp(keysPath, s => s.TryUnprotect(cipher)));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_secret_from_lost_keys_is_reported_as_unreadable_not_decrypted()
    {
        var oldKeys = Path.Combine(Path.GetTempPath(), $"erp-keys-old-{Guid.NewGuid():N}");
        var newKeys = Path.Combine(Path.GetTempPath(), $"erp-keys-new-{Guid.NewGuid():N}");
        try
        {
            var cipher = WithApp(oldKeys, s => s.Protect("paymob-secret"));

            Assert.Null(WithApp(newKeys, s => s.TryUnprotect(cipher)));
            Assert.ThrowsAny<CryptographicException>(() => WithApp(newKeys, s => s.Unprotect(cipher)));
        }
        finally
        {
            foreach (var dir in new[] { oldKeys, newKeys })
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
