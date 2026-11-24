using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// سكربت ترحيل البيانات docs/migrations/treasury-renumbering/renumber_treasury_accounts.sql على منشأة بشكل القاعدة القديمة:
/// البنك الافتراضي 1112 والبوابة 1113 وبنك مضاف 110001 وصندوق موقع 111001، بقيود ومراجع عليها. يتحقق أن الرموز تتوحد على التسلسل
/// مع بقاء المعرّفات والقيود والمراجع، وأن التجربة لا تغيّر شيئاً، وأن المنشأة ذات التعارض لا تُمس.
/// </summary>
[Collection("api")]
public class TreasuryRenumberingScriptTests : TestBase
{
    public TreasuryRenumberingScriptTests(ErpFactory f) : base(f) { }

    private async Task<T> WithDb<T>(Func<ERP.Service.Data.ErpDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>());
    }

    private static string ScriptText()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "docs", "migrations", "treasury-renumbering", "renumber_treasury_accounts.sql");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("renumber_treasury_accounts.sql");
    }

    private Task<int> RunScriptAsync(Guid tenant, bool dryRun) => WithDb(async db =>
    {
        var sql = ScriptText()
            .Replace("DECLARE @DryRun bit = 1;", $"DECLARE @DryRun bit = {(dryRun ? 1 : 0)};")
            .Replace("DECLARE @OnlyTenant uniqueidentifier = NULL;", $"DECLARE @OnlyTenant uniqueidentifier = '{tenant}';");
        db.Database.SetCommandTimeout(300);
        await db.Database.ExecuteSqlRawAsync(sql);
        return 0;
    });

    private static async Task PostEntryAsync(Client api, string debit, string credit, decimal amount)
    {
        var r = await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "اختبار",
            lines = new[] { new { accountCode = debit, debit = amount, credit = 0m }, new { accountCode = credit, debit = 0m, credit = amount } } });
        Assert.True(r.Status == 200, r.Body?.ToJsonString());
    }

    private sealed record Legacy(Guid DefaultBank, Guid Gateway, Guid AddedBank, Guid SiteCash, Guid BankId, Guid SiteId);

    /// <summary>منشأة بالترقيم القديم: 1112، 1113، بنك 110001، صندوق موقع 111001، طريقة دفع على 1112، وقيود على الأربعة.</summary>
    private async Task<Legacy> LegacyTenantAsync(Client api)
    {
        var bank = await api.Post("/banks", new { nameAr = "بنك قديم", nameEn = "Old bank" });
        Assert.True(bank.Status == 201, bank.Body?.ToJsonString());
        var site = await api.Post("/warehouses", new { code = "OLD", nameAr = "موقع قديم", nameEn = "Old site", location = "الرياض", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var bankId = Guid.Parse(bank.Data!["id"].S());
        var siteId = Guid.Parse(site.Data!["id"].S());
        var bankCode = bank.Data!["accountCode"].S();
        var siteCash = site.Data!["cashAccountCode"].S();

        var ids = await WithDb(async db =>
        {
            var accounts = await db.Set<Account>().IgnoreQueryFilters().Where(a => a.TenantId == api.TenantId).ToListAsync();
            Account Get(string code) => accounts.Single(a => a.Code == code);
            // الترتيب: الافتراضيان أولاً ثم بالرمز الحالي يُبعد كلاً عن الآخر في الفهرس الفريد
            Get(bankCode).Code = "110001";
            Get(siteCash).Code = "111001";
            await db.SaveChangesAsync();
            Get("1101").Code = "1112";
            Get("1102").Code = "1113";
            await db.SaveChangesAsync();
            (await db.Set<BankEntity>().IgnoreQueryFilters().SingleAsync(b => b.Id == bankId)).AccountCode = "110001";
            (await db.Set<Warehouse>().IgnoreQueryFilters().SingleAsync(w => w.Id == siteId)).CashAccountCode = "111001";
            // قاعدة قديمة: لا عدّادات للتسلسل الموحد
            db.RemoveRange(await db.Set<NumberSequence>().IgnoreQueryFilters()
                .Where(s => s.TenantId == api.TenantId && s.Key.StartsWith("treasury_code:")).ToListAsync());
            await db.SaveChangesAsync();
            return new Legacy(Get("1112").Id, Get("1113").Id, Get("110001").Id, Get("111001").Id, bankId, siteId);
        });

        Assert.Equal(201, (await api.Post("/paymentmethods", new { code = "BANK-OLD", nameAr = "تحويل", type = "bank", linkedAccountCode = "1112", status = "active" })).Status);
        await PostEntryAsync(api, "1112", "31", 700);
        await PostEntryAsync(api, "1113", "31", 50);
        await PostEntryAsync(api, "110001", "31", 300);
        await PostEntryAsync(api, "111001", "31", 20);
        return ids;
    }

    private Task<Dictionary<Guid, (string Code, string? Parent, int Lines, decimal Net)>> SnapshotAsync(Client api, params Guid[] ids) => WithDb(async db =>
    {
        var accounts = await db.Set<Account>().IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync();
        var result = new Dictionary<Guid, (string, string?, int, decimal)>();
        foreach (var a in accounts)
        {
            var lines = db.Set<JournalEntryLine>().IgnoreQueryFilters().Where(l => l.TenantId == api.TenantId && l.AccountCode == a.Code);
            result[a.Id] = (a.Code, a.ParentCode, await lines.CountAsync(), await lines.SumAsync(l => l.Debit - l.Credit));
        }
        return result;
    });

    [Fact]
    public async Task Script_renumbers_legacy_treasury_codes_keeping_ids_entries_and_references()
    {
        var api = await NewTenantAsync();
        var t = await LegacyTenantAsync(api);
        var all = new[] { t.DefaultBank, t.Gateway, t.AddedBank, t.SiteCash };
        var before = await SnapshotAsync(api, all);

        await RunScriptAsync(api.TenantId, dryRun: false);

        var after = await SnapshotAsync(api, all);
        Assert.Equal(("1101", "110"), (after[t.DefaultBank].Code, after[t.DefaultBank].Parent));
        Assert.Equal(("1102", "110"), (after[t.Gateway].Code, after[t.Gateway].Parent));
        Assert.Equal(("1103", "110"), (after[t.AddedBank].Code, after[t.AddedBank].Parent));
        Assert.Equal(("1112", "111"), (after[t.SiteCash].Code, after[t.SiteCash].Parent)); // كان 1112 للبنك: صار للصندوق
        foreach (var id in all)
            Assert.Equal((before[id].Lines, before[id].Net), (after[id].Lines, after[id].Net));
        Assert.True(after[t.SiteCash].Lines > 0 && after[t.SiteCash].Net == 20); // قيود الصندوق لم تختلط بقيود البنك القديمة 1112

        var refs = await WithDb(async db => (
            Bank: await db.Set<BankEntity>().IgnoreQueryFilters().Where(b => b.Id == t.BankId).Select(b => b.AccountCode).SingleAsync(),
            Site: await db.Set<Warehouse>().IgnoreQueryFilters().Where(w => w.Id == t.SiteId).Select(w => w.CashAccountCode).SingleAsync(),
            Method: await db.Set<PaymentMethodItem>().IgnoreQueryFilters().Where(p => p.TenantId == api.TenantId && p.Code == "BANK-OLD").Select(p => p.LinkedAccountCode).SingleAsync()));
        Assert.Equal(("1103", "1112", "1101"), (refs.Bank, refs.Site, refs.Method));

        // التسلسل يكمل بعد الترحيل
        var bank = await api.Post("/banks", new { nameAr = "بنك بعد الترحيل", nameEn = "After" });
        Assert.Equal("1104", bank.Data!["accountCode"].S());
        Assert.Equal("1113", (await api.Get("/accounts/treasury-next-code/111")).Data!["code"].S());
    }

    [Fact]
    public async Task Dry_run_changes_nothing()
    {
        var api = await NewTenantAsync();
        var t = await LegacyTenantAsync(api);
        var all = new[] { t.DefaultBank, t.Gateway, t.AddedBank, t.SiteCash };
        var before = await SnapshotAsync(api, all);

        await RunScriptAsync(api.TenantId, dryRun: true);

        Assert.Equal(before, await SnapshotAsync(api, all));
    }

    [Fact]
    public async Task Tenant_with_a_proposed_code_taken_elsewhere_is_left_untouched()
    {
        var api = await NewTenantAsync();
        var t = await LegacyTenantAsync(api);
        // 1103 (المقترح للبنك المضاف) حساب للمستخدم تحت 11: المنشأة تُستثنى للمراجعة اليدوية
        Assert.Equal(201, (await api.Post("/accounts", new { code = "1103", nameAr = "عهد", nameEn = "Custody", type = "asset", parentCode = "11", isDebitNature = true })).Status);
        var all = new[] { t.DefaultBank, t.Gateway, t.AddedBank, t.SiteCash };
        var before = await SnapshotAsync(api, all);

        await RunScriptAsync(api.TenantId, dryRun: false);

        Assert.Equal(before, await SnapshotAsync(api, all));
    }
}
