using System.Text.Json.Nodes;
using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// الصناديق (111) والبنوك (110) حسابان رئيسيان مستقلان تحت الأصول المتداولة (11): الصندوق العام وصناديق المواقع تحت الصناديق، والبنك الافتراضي
/// وتحصيلات البوابة والبنوك الجديدة تحت البنوك. هجرة <c>SeparateCashBoxesAndBanks</c> تنقل حسابات البنوك القائمة بالأب وحده (الرمز والرصيد كما هما)
/// وتعيد تسمية 111، والتشغيل الثاني لا يكرر. مطابقة البنك وقائمة التدفقات تقرآن المجموعتين.
/// </summary>
[Collection("api")]
public class CashBoxesAndBanksTests : TestBase
{
    public CashBoxesAndBanksTests(ErpFactory f) : base(f) { }

    private static async Task<JsonNode> AccountAsync(Client api, string code)
    {
        var r = await api.Get($"/accounts/ByCode/{code}");
        Assert.True(r.Status == 200, $"{code}: {r.Body?.ToJsonString()}");
        return r.Data!;
    }

    private static async Task<string> ParentAsync(Client api, string code) => (await AccountAsync(api, code))["parentCode"].S();

    private async Task<T> WithDb<T>(Func<ERP.Service.Data.ErpDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>());
    }

    /// <summary>نص الهجرة نفسه (Up) على قاعدة الاختبار: يُشغَّل مرتين ليثبت أنه لا يكرر.</summary>
    private Task<int> RunMigrationAsync() => WithDb(async db =>
    {
        var migration = new ERP.Service.Data.Migrations.SeparateCashBoxesAndBanks();
        foreach (var op in migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(op.Sql);
        return 0;
    });

    [Fact]
    public async Task New_company_has_cash_boxes_and_banks_as_separate_main_accounts_under_current_assets()
    {
        var api = await NewTenantAsync();
        var cash = await AccountAsync(api, "111");
        var banks = await AccountAsync(api, "110");
        Assert.Equal("الصناديق", cash["nameAr"].S());
        Assert.Equal("البنوك", banks["nameAr"].S());
        Assert.Equal("11", cash["parentCode"].S());
        Assert.Equal("11", banks["parentCode"].S());
        Assert.True(cash["isGroup"]!.GetValue<bool>());
        Assert.True(banks["isGroup"]!.GetValue<bool>());
        Assert.Equal("111", await ParentAsync(api, "1111"));
        Assert.Equal("110", await ParentAsync(api, "1112"));
        Assert.Equal("110", await ParentAsync(api, "1113"));

        var bank = await api.Post("/banks", new { nameAr = "بنك الاختبار", nameEn = "Test Bank", openingBalance = 500 });
        Assert.True(bank.Status == 201, bank.Body?.ToJsonString());
        var bankCode = bank.Data!["accountCode"].S();
        Assert.StartsWith("110", bankCode);
        Assert.Equal("110", await ParentAsync(api, bankCode));

        var site = await api.Post("/warehouses", new { code = "S1", nameAr = "موقع جدة", nameEn = "Jeddah", location = "جدة", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var siteCash = site.Data!["cashAccountCode"].S();
        Assert.StartsWith("111", siteCash);
        Assert.Equal("111", await ParentAsync(api, siteCash));
    }

    [Fact]
    public async Task Migration_moves_existing_bank_accounts_by_parent_only_and_does_not_duplicate()
    {
        var api = await NewTenantAsync();
        // منشأة بالشجرة القديمة: «النقدية والبنوك» 111 تحوي الصندوق والبنوك، ولا 110
        await WithDb(async db =>
        {
            var accounts = await db.Set<Account>().IgnoreQueryFilters().Where(a => a.TenantId == api.TenantId).ToListAsync();
            foreach (var a in accounts.Where(a => a.ParentCode == "110")) a.ParentCode = "111";
            var cash = accounts.Single(a => a.Code == "111");
            (cash.NameAr, cash.NameEn) = ("النقدية والبنوك", "Cash and Banks");
            db.Remove(accounts.Single(a => a.Code == "110"));
            var level = cash.Level + 1;
            db.Add(new Account
            {
                TenantId = api.TenantId, Code = "111005", NameAr = "بنك قديم", NameEn = "Old bank", Type = AccountCategory.Asset, ParentCode = "111",
                Level = level, IsDebitNature = true, Currency = cash.Currency, LinkedEntityType = LinkedEntityType.Bank, LinkedEntityId = Guid.NewGuid(),
            });
            db.Add(new Account
            {
                TenantId = api.TenantId, Code = "111006", NameAr = "صندوق - جدة", NameEn = "Cash box - Jeddah", Type = AccountCategory.Asset, ParentCode = "111",
                Level = level, IsDebitNature = true, Currency = cash.Currency, LinkedEntityType = LinkedEntityType.Warehouse, LinkedEntityId = Guid.NewGuid(),
            });
            var entry = new JournalEntry
            {
                TenantId = api.TenantId, EntryNumber = "OLD-" + Guid.NewGuid().ToString("N")[..6], Date = DateTime.UtcNow.Date, Description = "رصيد بنك قديم",
                SourceType = "test_old_bank", Status = JournalEntryStatus.Posted, TotalDebit = 300, TotalCredit = 300,
            };
            entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = "111005", AccountName = "بنك قديم", Debit = 300 });
            entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = "33", AccountName = "أرصدة افتتاحية", Credit = 300 });
            db.Add(entry);
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal(404, (await api.Get("/accounts/ByCode/110")).Status);
        var before = await LinesTotalAsync(api, "111005");

        await RunMigrationAsync();
        await RunMigrationAsync();

        Assert.Equal("البنوك", (await AccountAsync(api, "110"))["nameAr"].S());
        Assert.Equal("الصناديق", (await AccountAsync(api, "111"))["nameAr"].S());
        Assert.Equal("110", await ParentAsync(api, "111005"));  // البنك القديم برمزه
        Assert.Equal("110", await ParentAsync(api, "1112"));
        Assert.Equal("110", await ParentAsync(api, "1113"));
        Assert.Equal("111", await ParentAsync(api, "1111"));    // الصندوق
        Assert.Equal("111", await ParentAsync(api, "111006"));  // صندوق الموقع
        Assert.Equal(before, await LinesTotalAsync(api, "111005"));
        Assert.Equal(1, await WithDb(db => db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == api.TenantId && a.Code == "110")));

        // البنك القديم المنقول برمزه يبقى قابلاً للمطابقة، والمجموعتان لا
        var date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        Assert.Equal(200, (await api.Get($"/bankreconciliations/Worksheet?accountCode=111005&statementDate={date}")).Status);
        Assert.Equal(400, (await api.Get($"/bankreconciliations/Worksheet?accountCode=110&statementDate={date}")).Status);
        Assert.Equal(400, (await api.Get($"/bankreconciliations/Worksheet?accountCode=111&statementDate={date}")).Status);
    }

    private Task<decimal> LinesTotalAsync(Client api, string code)
        => WithDb(db => db.Set<JournalEntryLine>().IgnoreQueryFilters().Where(l => l.TenantId == api.TenantId && l.AccountCode == code).SumAsync(l => l.Debit - l.Credit));
}
