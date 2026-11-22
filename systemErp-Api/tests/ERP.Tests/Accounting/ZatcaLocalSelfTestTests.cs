using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ERP.Tests;

/// <summary>
/// التشخيص المحلي لمسار ما بعد إصدار الشهادة (<see cref="ZatcaLocalSelfTest"/>) بلا مضيف ويب: المراحل الثماني تنجح
/// ببيانات تجريبية فقط، الفشل يُحدَّد بمرحلته، الصنف معزول بنيوياً عن القاعدة والشبكة والهيئة، والتشغيل المتزامن مرفوض.
/// ضمن مجموعة «api» لأن قفل التشغيل الواحد على مستوى العملية يشترك فيه اختبار نقطة التشخيص.
/// </summary>
[Collection("api")]
public class ZatcaLocalSelfTestTests
{
    private static readonly string[] ExpectedStages =
    {
        ZatcaLocalSelfTest.StagePrivateKey, ZatcaLocalSelfTest.StageLocalCertificate, ZatcaLocalSelfTest.StageCertificateParsing,
        ZatcaLocalSelfTest.StageEncryption, ZatcaLocalSelfTest.StageComplianceXml, ZatcaLocalSelfTest.StageXmlSignature,
        ZatcaLocalSelfTest.StageSignatureVerification, ZatcaLocalSelfTest.StageEnvironmentCrypto,
    };

    private static ISecretProtector RealProtector() => new SecretProtector(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task All_eight_stages_pass_using_only_local_test_data()
    {
        var report = await new ZatcaLocalSelfTest(RealProtector(), NullLogger<ZatcaLocalSelfTest>.Instance).RunAsync();

        Assert.True(report.AllPassed, Describe(report));
        Assert.Null(report.FirstFailedStage);
        Assert.Equal(ExpectedStages, report.Stages.Select(s => s.Name));
        Assert.All(report.Stages, s => Assert.Equal("passed", s.Result));

        // الشهادة محلية تجريبية بوضوح، والبائع والرقم الضريبي تجريبيان (لا بيانات منشأة)
        var parsing = report.Stages.Single(s => s.Name == ZatcaLocalSelfTest.StageCertificateParsing).Detail!;
        Assert.Contains(ZatcaLocalSelfTest.TestMarker, parsing);
        Assert.Contains("LOCAL-DIAGNOSTIC-NOT-ZATCA", parsing);
        Assert.Contains("6 well-formed UBL documents", report.Stages.Single(s => s.Name == ZatcaLocalSelfTest.StageComplianceXml).Detail);

        // لا مفاتيح ولا شهادات ولا Stack Trace في النتيجة
        var json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("BEGIN", json);
        Assert.DoesNotContain("PRIVATE KEY", json);
        Assert.DoesNotContain("   at ", json);
        Assert.Contains("dotnet.runtime", report.Environment.Keys);
        Assert.Contains("crypto.ecdsaImplementation", report.Environment.Keys);
    }

    [Fact]
    public void The_self_test_is_structurally_isolated_from_the_database_network_and_ZATCA()
    {
        var type = typeof(ZatcaLocalSelfTest);
        var parameters = type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Equal(new[] { typeof(ISecretProtector), typeof(ILogger<ZatcaLocalSelfTest>) }, parameters);

        var forbidden = new[]
        {
            typeof(IZatcaApiClient), typeof(IZatcaService), typeof(IHttpClientFactory), typeof(HttpClient),
            typeof(Microsoft.EntityFrameworkCore.DbContext), typeof(ERP.Service.Data.ErpDbContext),
        };
        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType);
        Assert.DoesNotContain(fields, f => forbidden.Any(x => x.IsAssignableFrom(f)));
    }

    [Fact]
    public async Task A_failure_is_reported_at_its_stage_with_details_and_the_stack_trace_goes_only_to_the_log()
    {
        var log = new CapturingLogger();
        var report = await new ZatcaLocalSelfTest(new ThrowingProtector(), log).RunAsync();

        Assert.False(report.AllPassed);
        Assert.Equal(ZatcaLocalSelfTest.StageEncryption, report.FirstFailedStage);
        var failed = report.Stages.Single(s => s.Name == ZatcaLocalSelfTest.StageEncryption);
        Assert.Equal("failed", failed.Result);
        Assert.Equal(typeof(CryptographicException).FullName, failed.ExceptionType);
        Assert.Equal("simulated protector failure", failed.ExceptionMessage);
        Assert.Equal(new[] { "System.InvalidOperationException: simulated inner cause" }, failed.InnerExceptions);

        // المراحل التي لا تعتمد على التشفير تبقى تعمل، فيظهر موضع الفشل وحده
        Assert.All(report.Stages.Where(s => s.Name != ZatcaLocalSelfTest.StageEncryption), s => Assert.Equal("passed", s.Result));

        // الاستجابة بلا Stack Trace، والسجل يحمل الاستثناء كاملاً مع اسم المرحلة
        Assert.DoesNotContain("   at ", JsonSerializer.Serialize(report));
        var error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains(ZatcaLocalSelfTest.StageEncryption, error.Message);
        Assert.IsType<CryptographicException>(error.Exception);
        Assert.False(string.IsNullOrEmpty(error.Exception!.StackTrace));
    }

    [Fact]
    public async Task A_concurrent_run_is_rejected_while_one_is_in_progress()
    {
        var protector = new BlockingProtector();
        var first = new ZatcaLocalSelfTest(protector, NullLogger<ZatcaLocalSelfTest>.Instance).RunAsync();
        Assert.True(protector.Entered.Wait(TimeSpan.FromSeconds(30)), "the first run did not reach the encryption stage");

        await Assert.ThrowsAsync<ConflictException>(() => new ZatcaLocalSelfTest(RealProtector(), NullLogger<ZatcaLocalSelfTest>.Instance).RunAsync());

        protector.Release.Set();
        var report = await first;
        Assert.True(report.AllPassed, Describe(report));
    }

    private static string Describe(ERP.Core.DTOs.Accounting.Sales.ZatcaSelfTestReportDto report)
        => string.Join(" | ", report.Stages.Select(s => $"{s.Name}:{s.Result} {s.ExceptionType} {s.ExceptionMessage} {s.Detail}"));

    private sealed class ThrowingProtector : ISecretProtector
    {
        public string Protect(string plain) => throw new CryptographicException("simulated protector failure", new InvalidOperationException("simulated inner cause"));
        public string Unprotect(string cipher) => throw new CryptographicException("simulated protector failure");
    }

    /// <summary>يتوقف عند أول تشفير حتى يُفرج عنه، فيبقى التشغيل الأول جارياً أثناء محاولة التشغيل الثاني.</summary>
    private sealed class BlockingProtector : ISecretProtector
    {
        private readonly ISecretProtector _inner = RealProtector();
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public string Protect(string plain)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return _inner.Protect(plain);
        }
        public string Unprotect(string cipher) => _inner.Unprotect(cipher);
    }

    private sealed class CapturingLogger : ILogger<ZatcaLocalSelfTest>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
