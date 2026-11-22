using ERP.Api.Controllers.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// نقطة التشخيص المحلي: معطّلة افتراضياً (404)، للمالك والمدير فقط، ولا تتصل بالهيئة ولا تكتب في أي قاعدة ولا تمس جلسات
/// الربط. إثبات «لا كتابة»: عدّادات الإدراج والتحديث والحذف في SQL Server للقاعدة المشتركة وقواعد المنشآت لا تتغير.
/// </summary>
[Collection("api")]
public class ZatcaDiagnosticsEndpointTests : TestBase
{
    private const string Path = "/diagnostics/zatca/self-test";

    public ZatcaDiagnosticsEndpointTests(ErpFactory f) : base(f) { }

    private WebApplicationFactory<Program> Enabled()
        => Factory.WithWebHostBuilder(b => b.UseSetting(ZatcaDiagnosticsController.EnabledSetting, "true"));

    [Fact]
    public async Task The_endpoint_is_disabled_by_default_and_hidden_even_from_the_owner()
    {
        var owner = await NewTenantAsync();
        Assert.Equal(404, (await owner.Post(Path)).Status);
    }

    [Fact]
    public async Task When_enabled_it_runs_every_stage_without_calling_ZATCA_or_writing_to_any_database()
    {
        using var enabled = Enabled();
        var owner = await new Client(enabled.CreateClient()).RegisterAsync();
        var sessionsBefore = await ScalarAsync("SELECT COUNT(*) FROM ZatcaOnboardingSession");
        // مفتاح Data Protection يُنشأ عند أول استخدام في قاعدة جديدة؛ يُهيَّأ قبل القياس كما هو قائم أصلاً في الإنتاج
        using (var scope = enabled.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<ISecretProtector>().Protect("warm-up");

        var zatcaCalls = FakeZatcaApiClient.Instance.CallCount;
        var writes = await WriteCountersAsync();

        var res = await owner.Post(Path);

        Assert.Equal(200, res.Status);
        Assert.True(res.Data!["allPassed"]!.GetValue<bool>(), res.Body!.ToJsonString());
        Assert.Equal(8, res.Data["stages"]!.AsArray().Count);
        Assert.Equal(zatcaCalls, FakeZatcaApiClient.Instance.CallCount);
        Assert.Equal(writes, await WriteCountersAsync());
        Assert.Equal(sessionsBefore, await ScalarAsync("SELECT COUNT(*) FROM ZatcaOnboardingSession"));

        var raw = res.Body.ToJsonString();
        Assert.DoesNotContain("   at ", raw);
        Assert.DoesNotContain("PRIVATE KEY", raw);
    }

    [Fact]
    public async Task Only_the_owner_or_system_admin_may_run_it_even_when_enabled()
    {
        using var enabled = Enabled();
        var owner = await new Client(enabled.CreateClient()).RegisterAsync();

        // المحاسب الرئيسي يملك الصلاحيات الحسّاسة افتراضياً، لكن التشخيص للمالك والمدير فقط
        var accountant = await AddUserAsync(enabled, owner, "chief_accountant");
        Assert.Equal(403, (await accountant.Post(Path)).Status);

        // دور بلا الصلاحية المستقلة
        var rep = await AddUserAsync(enabled, owner, "sales_rep");
        Assert.Equal(403, (await rep.Post(Path)).Status);
    }

    private static async Task<Client> AddUserAsync(WebApplicationFactory<Program> host, Client owner, string role)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = role, email, phone = "1", password = "Passw0rd!", role })).Status);
        return await owner.LoginAsAsync(host.CreateClient(), email, "Passw0rd!");
    }

    /// <summary>مجموع عمليات الإدراج والتحديث والحذف على جداول المستخدم في قاعدة الاختبار وقواعد منشآتها المستقلة.</summary>
    private async Task<long> WriteCountersAsync()
    {
        var database = new SqlConnectionStringBuilder(Factory.ConnectionString).InitialCatalog;
        await using var conn = new SqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ISNULL(SUM(s.leaf_insert_count + s.leaf_update_count + s.leaf_delete_count), 0)
            FROM sys.dm_db_index_operational_stats(NULL, NULL, NULL, NULL) s
            JOIN sys.databases d ON d.database_id = s.database_id
            WHERE (d.name = @db OR d.name LIKE @db + '[_]t%') AND s.object_id > 100
            """;
        cmd.Parameters.AddWithValue("@db", database);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> ScalarAsync(string sql)
    {
        await using var conn = new SqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
