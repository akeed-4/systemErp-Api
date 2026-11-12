using ERP.Core.Models.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.Tests;

/// <summary>
/// هجرة مكتبة التكاليف على قاعدة فيها موارد قبلها: سطر افتتاحي واحد لكل مورد بتكلفته ومصدره ووحدته وساعاته، سارياً من آخر ضبط
/// لتكلفته (أو إنشائه إن لم تُضبط)، بعملة منشأته (أو SAR بلا منشأة)، والتكلفة المخزّنة تأخذ تاريخه ولا تتغير قيمتها.
/// إعادة الهجرة (رجوع ثم تطبيق، أو تطبيق مكرر) لا تكرر الأسطر.
/// </summary>
public class ConstructionCostLibraryMigrationTests
{
    private const string Before = "20261111090000_AddZatcaOnboardingSessions";
    private const string Target = "20261111204011_ConstructionCostLibrary";

    private sealed record Row(Guid ResourceId, Guid TenantId, DateTime EffectiveFrom, decimal UnitCost, string Currency, string Source,
        string Unit, decimal HoursPerUnit, string? Reason);

    [Fact]
    public async Task Existing_resources_get_one_opening_history_row_with_their_cost_and_date()
    {
        var baseConn = Environment.GetEnvironmentVariable("ERP_TEST_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True;";
        var connection = $"{baseConn.TrimEnd(';')};Database=ErpMigTest_{Guid.NewGuid():N};MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName)).Options;

        await using var db = new ErpDbContext(options, new TenantContext());
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(Before);

            // منشأة بعملة AED، وموارد قائمة قبل الهجرة (الأعمدة الجديدة غير موجودة بعد: إدخال مباشر)
            var tenant = new Tenant { NameAr = "منشأة الهجرة", NameEn = "Migration Co", Code = "MIG" + Guid.NewGuid().ToString("N")[..6], Currency = "AED" };
            db.Add(tenant);
            await db.SaveChangesAsync();
            var orphanTenant = Guid.NewGuid();
            var updated = Guid.NewGuid();
            var neverUpdated = Guid.NewGuid();
            var orphan = Guid.NewGuid();
            await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO [ConstructionResource] ([Id],[TenantId],[CreatedAt],[Code],[NameAr],[NameEn],[ResourceType],[Unit],[StandardCost],[CostSource],
    [CostUpdatedAt],[HoursPerUnit],[HrEmployeeCount],[IsActive])
VALUES
 ({0},{1},'2026-01-01T09:00:00',N'MAT-0001',N'رمل',N'',N'material',N'M3',12.5,N'manual','2026-03-05T22:30:00',1,0,1),
 ({2},{1},'2026-02-10T08:00:00',N'LAB-0001',N'عامل',N'',N'labor',N'HR',300,N'hr_job',NULL,8,3,1),
 ({3},{4},'2026-04-01T00:00:00',N'EQP-0001',N'حفار',N'',N'equipment',N'HR',180,N'manual','2026-04-02T01:00:00',1,0,0);",
                updated, tenant.Id, neverUpdated, orphan, orphanTenant);

            await migrator.MigrateAsync(Target);
            await AssertOpeningRowsAsync(connection, tenant.Id, orphanTenant, updated, neverUpdated, orphan);

            // تطبيق مكرر لا يعيد الهجرة، والرجوع ثم التطبيق يعيد بناء السطر الافتتاحي مرة واحدة
            await migrator.MigrateAsync(Target);
            await AssertOpeningRowsAsync(connection, tenant.Id, orphanTenant, updated, neverUpdated, orphan);
            await migrator.MigrateAsync(Before);
            await migrator.MigrateAsync(Target);
            await AssertOpeningRowsAsync(connection, tenant.Id, orphanTenant, updated, neverUpdated, orphan);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertOpeningRowsAsync(string connection, Guid tenant, Guid orphanTenant, Guid updated, Guid neverUpdated, Guid orphan)
    {
        var rows = await RowsAsync(connection);
        Assert.Equal(3, rows.Count); // سطر واحد لكل مورد، بلا تكرار
        Assert.All(rows, r => Assert.Equal("migration", r.Reason));

        var a = rows.Single(r => r.ResourceId == updated);
        Assert.Equal((tenant, new DateTime(2026, 3, 5), 12.5m, "AED", "manual", "M3", 1m), (a.TenantId, a.EffectiveFrom, a.UnitCost, a.Currency, a.Source, a.Unit, a.HoursPerUnit));

        var b = rows.Single(r => r.ResourceId == neverUpdated); // بلا ضبط للتكلفة: من تاريخ الإنشاء
        Assert.Equal((tenant, new DateTime(2026, 2, 10), 300m, "AED", "hr_job", "HR", 8m), (b.TenantId, b.EffectiveFrom, b.UnitCost, b.Currency, b.Source, b.Unit, b.HoursPerUnit));

        var c = rows.Single(r => r.ResourceId == orphan); // منشأة بلا سجل منشأة: العملة الافتراضية، والمورد الموقوف مشمول
        Assert.Equal((orphanTenant, new DateTime(2026, 4, 2), 180m, "SAR"), (c.TenantId, c.EffectiveFrom, c.UnitCost, c.Currency));

        // التكلفة المخزّنة بقيمتها، وتاريخ سريانها = السطر الافتتاحي
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var cmd = new SqlCommand("SELECT [Id],[StandardCost],[CostEffectiveFrom] FROM [ConstructionResource]", sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        var cache = new Dictionary<Guid, (decimal Cost, DateTime? From)>();
        while (await reader.ReadAsync())
            cache[reader.GetGuid(0)] = (reader.GetDecimal(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2));
        Assert.Equal((12.5m, (DateTime?)new DateTime(2026, 3, 5)), cache[updated]);
        Assert.Equal((300m, (DateTime?)new DateTime(2026, 2, 10)), cache[neverUpdated]);
        Assert.Equal((180m, (DateTime?)new DateTime(2026, 4, 2)), cache[orphan]);
    }

    private static async Task<List<Row>> RowsAsync(string connection)
    {
        await using var sql = new SqlConnection(connection);
        await sql.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT [ResourceId],[TenantId],[EffectiveFrom],[UnitCost],[Currency],[Source],[Unit],[HoursPerUnit],[Reason] FROM [ConstructionResourceCost]", sql);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await reader.ReadAsync())
            rows.Add(new Row(reader.GetGuid(0), reader.GetGuid(1), reader.GetDateTime(2), reader.GetDecimal(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetDecimal(7), reader.IsDBNull(8) ? null : reader.GetString(8)));
        return rows;
    }
}
