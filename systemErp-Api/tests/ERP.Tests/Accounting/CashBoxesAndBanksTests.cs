using System.Text.Json.Nodes;
using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// الصندوق/النقدية (111) والبنوك (110) حسابان رئيسيان مستقلان تحت الأصول المتداولة (11): الصندوق العام وصناديق المواقع تحت النقدية، والبنك الافتراضي
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

    /// <summary>نص الهجرتين نفسه (Up) على قاعدة الاختبار: الفصل ثم تثبيت الاسمين والرئيسيين. يُشغَّل مرتين ليثبت أنه لا يكرر.</summary>
    private Task<int> RunMigrationAsync() => WithDb(async db =>
    {
        Microsoft.EntityFrameworkCore.Migrations.Migration[] migrations =
            { new ERP.Service.Data.Migrations.SeparateCashBoxesAndBanks(), new ERP.Service.Data.Migrations.CashAndBanksMainAccounts() };
        foreach (var migration in migrations)
            foreach (var op in migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>())
                await db.Database.ExecuteSqlRawAsync(op.Sql);
        return 0;
    });

    [Fact]
    public async Task Database_already_migrated_with_the_old_cash_name_is_renamed_to_cash_main_account()
    {
        var api = await NewTenantAsync();
        await WithDb(async db =>
        {
            var cash = await db.Set<Account>().IgnoreQueryFilters().SingleAsync(a => a.TenantId == api.TenantId && a.Code == "111");
            (cash.NameAr, cash.NameEn) = ("الصناديق", "Cash Boxes"); // اسم الهجرة قبل تعديلها
            var main = await db.Set<Account>().IgnoreQueryFilters().SingleAsync(a => a.TenantId == api.TenantId && a.Code == "1111");
            (main.NameAr, main.NameEn) = ("الصندوق", "Cash on Hand"); // الاسم الافتراضي السابق
            await db.SaveChangesAsync();
            return 0;
        });
        var before = await LinesTotalAsync(api, "1111");

        await RunMigrationAsync();

        var renamed = await AccountAsync(api, "111");
        Assert.Equal("الصندوق/النقدية", renamed["nameAr"].S());
        Assert.Equal("Cash Boxes", renamed["nameEn"].S());
        Assert.True(renamed["isGroup"]!.GetValue<bool>());
        Assert.Equal("الصندوق الرئيسي", (await AccountAsync(api, "1111"))["nameAr"].S());
        Assert.Equal("111", await ParentAsync(api, "1111"));
        Assert.Equal("البنوك", (await AccountAsync(api, "110"))["nameAr"].S());
        Assert.Equal("110", await ParentAsync(api, "1101"));
        Assert.Equal("110", await ParentAsync(api, "1102"));
        Assert.Equal(before, await LinesTotalAsync(api, "1111"));
    }

    [Fact]
    public async Task Site_cash_box_is_under_111_linked_to_its_site_and_never_duplicated()
    {
        var api = await NewTenantAsync();
        var site = await api.Post("/warehouses", new { code = "S4", nameAr = "موقع تبوك", nameEn = "Tabuk", location = "تبوك", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var id = Guid.Parse(site.Data!["id"].S());
        var cashCode = site.Data!["cashAccountCode"].S();
        var cash = await AccountAsync(api, cashCode);
        Assert.Equal("111", cash["parentCode"].S());
        Assert.Equal(id.ToString(), cash["linkedEntityId"].S());

        // فُقد ربط الصندوق بالموقع: «إنشاء حسابات المواقع» يعيد ربط صندوقه نفسه ولا ينشئ صندوقاً ثانياً
        await WithDb(async db =>
        {
            var w = await db.Set<Warehouse>().IgnoreQueryFilters().SingleAsync(x => x.Id == id);
            w.CashAccountCode = null;
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.True((await api.Post("/warehouses/site-accounts", new { })).Status is 200 or 201);
        Assert.Equal(cashCode, (await api.Get($"/warehouses/{id}")).Data!["cashAccountCode"].S());
        Assert.Equal(1, await WithDb(db => db.Set<Account>().IgnoreQueryFilters()
            .CountAsync(a => a.TenantId == api.TenantId && a.LinkedEntityId == id && a.ParentCode == "111")));
    }

    [Fact]
    public async Task New_company_has_cash_boxes_and_banks_as_separate_main_accounts_under_current_assets()
    {
        var api = await NewTenantAsync();
        var cash = await AccountAsync(api, "111");
        var banks = await AccountAsync(api, "110");
        Assert.Equal("الصندوق/النقدية", cash["nameAr"].S());
        Assert.Equal("البنوك", banks["nameAr"].S());
        Assert.Equal("11", cash["parentCode"].S());
        Assert.Equal("11", banks["parentCode"].S());
        Assert.True(cash["isGroup"]!.GetValue<bool>());
        Assert.True(banks["isGroup"]!.GetValue<bool>());
        Assert.Equal("111", await ParentAsync(api, "1111"));
        Assert.Equal("110", await ParentAsync(api, "1101")); // البنك الافتراضي: أول تسلسل البنوك
        Assert.Equal("110", await ParentAsync(api, "1102")); // تحصيلات البوابة

        var bank = await api.Post("/banks", new { nameAr = "بنك الاختبار", nameEn = "Test Bank", openingBalance = 500 });
        Assert.True(bank.Status == 201, bank.Body?.ToJsonString());
        var bankCode = bank.Data!["accountCode"].S();
        Assert.Equal("1103", bankCode); // أول بنك مضاف بعد الافتراضيين
        Assert.Equal("110", await ParentAsync(api, bankCode));

        var site = await api.Post("/warehouses", new { code = "S1", nameAr = "موقع جدة", nameEn = "Jeddah", location = "جدة", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var siteCash = site.Data!["cashAccountCode"].S();
        Assert.Equal("1112", siteCash); // أول صندوق بعد الصندوق الرئيسي
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
            OldSeedCodes(accounts);
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
        Assert.Equal("الصندوق/النقدية", (await AccountAsync(api, "111"))["nameAr"].S());
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

    private static string Today => DateTime.UtcNow.Date.ToString("yyyy-MM-dd");

    private static async Task PostEntryAsync(Client api, string debit, string credit, decimal amount)
    {
        var r = await api.Post("/journalentries", new { date = DateTime.UtcNow, description = "اختبار",
            lines = new[] { new { accountCode = debit, debit = amount, credit = 0m }, new { accountCode = credit, debit = 0m, credit = amount } } });
        Assert.True(r.Status == 200, r.Body?.ToJsonString());
    }

    private static async Task<JsonNode> CashFlowAsync(Client api)
        => (await api.Get($"/financialstatements/CashFlow?from={Today}&to={Today}")).Data!;

    /// <summary>
    /// شجرة قديمة قبل الفصل: «النقدية والبنوك» 111 تحوي الصندوق والبنوك، ولا مجموعة بنوك نظامية. <paramref name="userAccount110"/>: حساب
    /// أنشأه المستخدم بالرمز 110 (عهد موظفين، ليس نقدية) عليه قيد 250.
    /// </summary>
    private Task<int> LegacyTreeAsync(Client api, bool userAccount110) => WithDb(async db =>
    {
        var accounts = await db.Set<Account>().IgnoreQueryFilters().Where(a => a.TenantId == api.TenantId).ToListAsync();
        OldSeedCodes(accounts);
        foreach (var a in accounts.Where(a => a.ParentCode == "110")) a.ParentCode = "111";
        var cash = accounts.Single(a => a.Code == "111");
        (cash.NameAr, cash.NameEn) = ("النقدية والبنوك", "Cash and Banks");
        db.Remove(accounts.Single(a => a.Code == "110"));
        await db.SaveChangesAsync();
        db.Add(new Account
        {
            TenantId = api.TenantId, Code = "111005", NameAr = "بنك قديم", NameEn = "Old bank", Type = AccountCategory.Asset, ParentCode = "111",
            Level = cash.Level + 1, IsDebitNature = true, Currency = cash.Currency, LinkedEntityType = LinkedEntityType.Bank, LinkedEntityId = Guid.NewGuid(),
        });
        if (userAccount110)
        {
            db.Add(new Account
            {
                TenantId = api.TenantId, Code = "110", NameAr = "عهد الموظفين", NameEn = "Staff custody", Type = AccountCategory.Asset, ParentCode = "11",
                Level = cash.Level, IsDebitNature = true, Currency = cash.Currency, IsSystem = false, IsGroup = false,
            });
            var entry = new JournalEntry
            {
                TenantId = api.TenantId, EntryNumber = "OLD-" + Guid.NewGuid().ToString("N")[..6], Date = DateTime.UtcNow.Date, Description = "عهدة",
                SourceType = "test_custody", Status = JournalEntryStatus.Posted, TotalDebit = 250, TotalCredit = 250,
            };
            entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = "110", AccountName = "عهد الموظفين", Debit = 250 });
            entry.Lines.Add(new JournalEntryLine { TenantId = api.TenantId, AccountCode = "33", AccountName = "أرصدة افتتاحية", Credit = 250 });
            db.Add(entry);
        }
        await db.SaveChangesAsync();
        return 0;
    });

    [Fact]
    public async Task Account_whose_code_starts_with_110_or_111_but_is_outside_the_cash_groups_is_not_cash()
    {
        var api = await NewTenantAsync();
        // رمز يبدأ بـ 110 تحت الأصول المتداولة 11 لا تحت البنوك — أصل متداول غير نقدي
        Assert.Equal(201, (await api.Post("/accounts", new { code = "1109", nameAr = "عهد", nameEn = "Custody", type = "asset", parentCode = "11", isDebitNature = true })).Status);
        await PostEntryAsync(api, "1109", "31", 1000);

        Assert.Equal(400, (await api.Get($"/bankreconciliations/Worksheet?accountCode=1109&statementDate={Today}")).Status);
        Assert.Equal(200, (await api.Get($"/bankreconciliations/Worksheet?accountCode=1101&statementDate={Today}")).Status);
        Assert.Equal(200, (await api.Get($"/bankreconciliations/Worksheet?accountCode=1111&statementDate={Today}")).Status);

        var flow = await CashFlowAsync(api);
        Assert.Equal(0, flow["financing"]!["total"].D());
        Assert.Equal(0, flow["closingCash"]!["total"].D());
        Assert.Equal(0, (await api.Get("/reports/FinancialStats")).Data!["cashAndBankBalance"].D());

        // حركة نقدية حقيقية تُحتسب: الصندوق والبنك
        await PostEntryAsync(api, "1111", "31", 300);
        await PostEntryAsync(api, "1101", "31", 200);
        flow = await CashFlowAsync(api);
        Assert.Equal(500, flow["financing"]!["total"].D());
        Assert.Equal(500, flow["closingCash"]!["total"].D());
        Assert.Equal(500, (await api.Get("/reports/FinancialStats")).Data!["cashAndBankBalance"].D());
    }

    [Fact]
    public async Task User_account_coded_110_is_detected_left_untouched_and_banks_stay_under_111()
    {
        var api = await NewTenantAsync();
        await LegacyTreeAsync(api, userAccount110: true);
        var before = await LinesTotalAsync(api, "110");

        await RunMigrationAsync();

        var custody = await AccountAsync(api, "110");
        Assert.Equal("عهد الموظفين", custody["nameAr"].S());       // لا إعادة تسمية
        Assert.Equal("11", custody["parentCode"].S());
        Assert.False(custody["isSystem"]!.GetValue<bool>());
        Assert.Equal("111", await ParentAsync(api, "1112"));      // لا نقل
        Assert.Equal("111", await ParentAsync(api, "111005"));
        Assert.Equal("النقدية والبنوك", (await AccountAsync(api, "111"))["nameAr"].S());
        Assert.Equal(before, await LinesTotalAsync(api, "110"));

        // البنك الجديد تحت 111 لا تحت حساب المستخدم
        var bank = await api.Post("/banks", new { nameAr = "بنك جديد", nameEn = "New Bank" });
        Assert.True(bank.Status == 201, bank.Body?.ToJsonString());
        Assert.Equal("111", await ParentAsync(api, bank.Data!["accountCode"].S()));

        // حساب المستخدم ليس نقدية: لا تسوية ولا تدفق نقدي ولا رصيد نقدية
        Assert.Equal(400, (await api.Get($"/bankreconciliations/Worksheet?accountCode=110&statementDate={Today}")).Status);
        Assert.Equal(200, (await api.Get($"/bankreconciliations/Worksheet?accountCode=111005&statementDate={Today}")).Status);
        Assert.Equal(0, (await CashFlowAsync(api))["closingCash"]!["total"].D());
        Assert.Equal(0, (await api.Get("/reports/FinancialStats")).Data!["cashAndBankBalance"].D());
        Assert.Equal(before, await LinesTotalAsync(api, "110"));
    }

    [Fact]
    public async Task Migration_makes_cash_boxes_and_banks_two_main_accounts_without_changing_balances_or_cash()
    {
        var api = await NewTenantAsync();
        await LegacyTreeAsync(api, userAccount110: false);
        await PostEntryAsync(api, "111005", "33", 400);
        await PostEntryAsync(api, "1111", "33", 100);
        var bankLines = await LinesTotalAsync(api, "111005");
        var flowBefore = await CashFlowAsync(api);
        Assert.Equal(500, flowBefore["closingCash"]!["total"].D()); // الشجرة القديمة: كل ما تحت 111 نقدية

        await RunMigrationAsync();

        var banks = await AccountAsync(api, "110");
        var cashBoxes = await AccountAsync(api, "111");
        Assert.True(banks["isGroup"]!.GetValue<bool>());
        Assert.True(cashBoxes["isGroup"]!.GetValue<bool>());
        Assert.Equal("11", banks["parentCode"].S());
        Assert.Equal("11", cashBoxes["parentCode"].S());
        Assert.Equal("110", await ParentAsync(api, "111005"));
        Assert.Equal("110", await ParentAsync(api, "1112"));
        Assert.Equal("110", await ParentAsync(api, "1113"));
        Assert.Equal("111", await ParentAsync(api, "1111"));
        Assert.Equal("الصندوق/النقدية", (await AccountAsync(api, "111"))["nameAr"].S());
        Assert.True((await AccountAsync(api, "110"))["isSystem"]!.GetValue<bool>());
        Assert.Equal(bankLines, await LinesTotalAsync(api, "111005"));
        Assert.Equal(500, (await CashFlowAsync(api))["closingCash"]!["total"].D()); // الأرصدة كما هي
        Assert.Equal(1, await WithDb(db => db.Set<Account>().IgnoreQueryFilters().CountAsync(a => a.TenantId == api.TenantId && a.Code == "110")));
    }

    [Fact]
    public async Task Perpetual_sites_get_no_opening_or_closing_inventory_until_periodic_is_chosen()
    {
        var api = await NewTenantAsync();
        var site = await api.Post("/warehouses", new { code = "S2", nameAr = "موقع الدمام", nameEn = "Dammam", location = "الدمام", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var id = site.Data!["id"].S();
        Assert.Null(site.Data!["openingInventoryAccountCode"]?.GetValue<string?>());
        Assert.Null(site.Data!["endingInventoryAccountCode"]?.GetValue<string?>());
        var cashCode = site.Data!["cashAccountCode"].S();

        var saved = await api.Put("/accounting-settings", new { inventoryMethod = "periodic" });
        Assert.True(saved.Status == 200, saved.Body?.ToJsonString());

        var w = (await api.Get($"/warehouses/{id}")).Data!;
        var opening = w["openingInventoryAccountCode"].S();
        var ending = w["endingInventoryAccountCode"].S();
        Assert.False(string.IsNullOrEmpty(opening));
        Assert.False(string.IsNullOrEmpty(ending));
        Assert.Equal(cashCode, w["cashAccountCode"].S()); // الحسابات السابقة كما هي
        var s = (await api.Get("/accounting-settings")).Data!;
        Assert.Equal(await ParentAsync(api, s["openingInventoryAccountCode"].S()), await ParentAsync(api, opening));
        Assert.Equal(await ParentAsync(api, s["endingInventoryAccountCode"].S()), await ParentAsync(api, ending));
    }

    [Fact]
    public async Task Site_periodic_accounts_show_in_cost_of_sales_whatever_their_parent_type()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/accounting-settings", new { inventoryMethod = "periodic" })).Status);
        var site = await api.Post("/warehouses", new { code = "S3", nameAr = "موقع مكة", nameEn = "Makkah", location = "مكة", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        var id = Guid.Parse(site.Data!["id"].S());
        var opening = site.Data!["openingInventoryAccountCode"].S();
        Assert.False(string.IsNullOrEmpty(opening));

        // آخر المدة على حساب قديم من نوع إيراد (تحت الإيرادات الأخرى)
        Assert.Equal(201, (await api.Post("/accounts", new { code = "4299", nameAr = "بضاعة آخر المدة - قديم", nameEn = "Legacy closing", type = "revenue", parentCode = "42", isDebitNature = false })).Status);
        await WithDb(async db =>
        {
            var w = await db.Set<Warehouse>().IgnoreQueryFilters().SingleAsync(x => x.Id == id);
            w.EndingInventoryAccountCode = "4299";
            await db.SaveChangesAsync();
            return 0;
        });
        await PostEntryAsync(api, "1111", "4299", 100);
        await PostEntryAsync(api, opening, "1111", 40);

        var income = (await api.Get($"/financialstatements/IncomeStatement?from={Today}&to={Today}")).Data!;
        static IEnumerable<string> Codes(JsonNode section) => section["lines"]!.AsArray().Select(l => l!["accountCode"].S());
        Assert.Contains("4299", Codes(income["costOfSales"]!));
        Assert.Contains(opening, Codes(income["costOfSales"]!));
        Assert.DoesNotContain("4299", Codes(income["revenue"]!));
        Assert.DoesNotContain(opening, Codes(income["operatingExpenses"]!));
        Assert.Equal(-60, income["costOfSales"]!["total"].D()); // أول المدة 40 − آخر المدة 100
        Assert.Equal(0, income["revenue"]!["total"].D());
    }

    /// <summary>رموز التأسيس قبل توحيد تسلسل البنوك (البنك الافتراضي 1112 والبوابة 1113) لمحاكاة قاعدة قديمة.</summary>
    private static void OldSeedCodes(List<Account> accounts)
    {
        foreach (var a in accounts)
            a.Code = a.Code switch { "1101" => "1112", "1102" => "1113", _ => a.Code };
    }

    private static async Task<string> NewBankAsync(Client api, string name)
    {
        var r = await api.Post("/banks", new { nameAr = name, nameEn = name });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        return r.Data!["accountCode"].S();
    }

    private static async Task<string> NewAccountUnderAsync(Client api, string parent, string name, string code = "")
    {
        var r = await api.Post("/accounts", new { code, nameAr = name, nameEn = name, type = "asset", parentCode = parent, isDebitNature = true });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
        return r.Data!["code"].S();
    }

    [Fact]
    public async Task Banks_and_cash_boxes_share_one_sequence_with_their_default_accounts_per_group()
    {
        var api = await NewTenantAsync();
        Assert.Equal("1103", (await api.Get("/accounts/treasury-next-code/110")).Data!["code"].S());
        Assert.Equal("1112", (await api.Get("/accounts/treasury-next-code/111")).Data!["code"].S());

        // البنوك: الافتراضيان 1101، 1102 ثم المضاف من شاشة البنوك أو الشجرة على التسلسل نفسه
        Assert.Equal("1103", await NewBankAsync(api, "بنك أ"));
        Assert.Equal("1104", await NewAccountUnderAsync(api, "110", "بنك من الشجرة"));
        // الرمز المرسل يدوياً تحت المجموعة لا يكسر التسلسل
        Assert.Equal("1105", await NewAccountUnderAsync(api, "110", "بنك برمز يدوي", code: "110777"));

        // الصناديق مستقلة: الرئيسي 1111 ثم صندوق الموقع والمضاف من الشجرة
        var site = await api.Post("/warehouses", new { code = "SQ", nameAr = "موقع القصيم", nameEn = "Qassim", location = "القصيم", status = "active" });
        Assert.True(site.Status == 201, site.Body?.ToJsonString());
        Assert.Equal("1112", site.Data!["cashAccountCode"].S());
        Assert.Equal("1113", await NewAccountUnderAsync(api, "111", "صندوق فرعي"));
        Assert.Equal("1106", await NewBankAsync(api, "بنك ب")); // البنوك لم تتأثر بالصناديق
    }

    [Fact]
    public async Task Deleted_bank_code_is_not_reused_and_codes_used_elsewhere_are_skipped()
    {
        var api = await NewTenantAsync();
        var bank = await api.Post("/banks", new { nameAr = "بنك مؤقت", nameEn = "Temp" });
        Assert.Equal("1103", bank.Data!["accountCode"].S());
        Assert.Equal(200, (await api.Delete($"/banks/{bank.Data!["id"].S()}")).Status);
        Assert.Equal("1104", await NewBankAsync(api, "بعد الحذف"));

        // 1105 مستخدم لحساب تحت أب آخر (11): التسلسل يتخطاه بدل التعارض
        Assert.Equal(201, (await api.Post("/accounts", new { code = "1105", nameAr = "عهد", nameEn = "Custody", type = "asset", parentCode = "11", isDebitNature = true })).Status);
        Assert.Equal("1106", await NewBankAsync(api, "بعد المشغول"));
        Assert.Equal("11", await ParentAsync(api, "1105"));
    }

    [Fact]
    public async Task Sequence_continues_after_nine_and_tree_orders_codes_numerically()
    {
        var api = await NewTenantAsync();
        var codes = new List<string>();
        for (var i = 0; i < 8; i++) codes.Add(await NewBankAsync(api, $"بنك {i}"));
        Assert.Equal(new[] { "1103", "1104", "1105", "1106", "1107", "1108", "1109", "11010" }, codes);
        Assert.Equal("11011", (await api.Get("/accounts/treasury-next-code/110")).Data!["code"].S());

        static IEnumerable<JsonNode> Flatten(JsonNode n) => new[] { n }.Concat((n["children"]?.AsArray() ?? new JsonArray()).SelectMany(c => Flatten(c!)));
        var tree = (await api.Get("/accounts/tree")).Data!.AsArray().SelectMany(n => Flatten(n!));
        var banks = tree.Single(a => a["code"].S() == "110")["children"]!.AsArray().Select(c => c!["code"].S()).ToList();
        Assert.Equal(new[] { "1101", "1102", "1103", "1104", "1105", "1106", "1107", "1108", "1109", "11010" }, banks);
    }

    [Fact]
    public async Task Concurrent_bank_and_cash_box_creation_never_duplicates_codes()
    {
        var api = await NewTenantAsync();
        var banks = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => api.Post("/banks", new { nameAr = $"بنك متزامن {i}", nameEn = $"B{i}" })));
        var boxes = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            api.Post("/accounts", new { code = "", nameAr = $"صندوق متزامن {i}", nameEn = $"C{i}", type = "asset", parentCode = "111", isDebitNature = true })));
        Assert.All(banks, r => Assert.True(r.Status == 201, r.Body?.ToJsonString()));
        Assert.All(boxes, r => Assert.True(r.Status == 201, r.Body?.ToJsonString()));

        var bankCodes = banks.Select(r => r.Data!["accountCode"].S()).OrderBy(c => c.Length).ThenBy(c => c).ToList();
        var boxCodes = boxes.Select(r => r.Data!["code"].S()).OrderBy(c => c.Length).ThenBy(c => c).ToList();
        Assert.Equal(new[] { "1103", "1104", "1105", "1106", "1107", "1108" }, bankCodes);
        Assert.Equal(new[] { "1112", "1113", "1114", "1115", "1116", "1117" }, boxCodes);
        foreach (var c in bankCodes) Assert.Equal("110", await ParentAsync(api, c));
        foreach (var c in boxCodes) Assert.Equal("111", await ParentAsync(api, c));
    }

    private Task<decimal> LinesTotalAsync(Client api, string code)
        => WithDb(db => db.Set<JournalEntryLine>().IgnoreQueryFilters().Where(l => l.TenantId == api.TenantId && l.AccountCode == code).SumAsync(l => l.Debit - l.Credit));
}
