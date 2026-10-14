using System.Globalization;
using System.Text.Json.Nodes;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace ERP.Tests;

/// <summary>
/// سلامة الدفاتر: التقريب قبل التوازن، الرصيد = مجموع الحركات (وإصلاحه)، عرض الرصيد بإشارته، الإلغاء بقيد عكسي بدل الحذف،
/// تاريخ الأعمال بتوقيت المملكة، ومطابقة المخزون مع الأستاذ.
/// </summary>
[Collection("api")]
public class BooksIntegrityTests : TestBase
{
    public BooksIntegrityTests(ErpFactory f) : base(f) { }

    private static object Entry(params (string Code, decimal Debit, decimal Credit)[] lines)
        => new { description = "قيد اختبار", lines = lines.Select(l => new { accountCode = l.Code, debit = l.Debit, credit = l.Credit }).ToArray() };

    private static async Task EquityAccountAsync(Client api, string code)
        => Assert.Equal(201, (await api.Post("/accounts", new { code, nameAr = "رأس مال " + code, nameEn = "Capital " + code, type = "equity", parentCode = "31", isDebitNature = false })).Status);

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private async Task CorruptBalanceAsync(Client api, string code, decimal delta)
    {
        var id = (await api.Get($"/accounts/ByCode/{code}")).Data!["id"].G();
        await using var conn = new SqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Account SET Balance = Balance + @delta WHERE Id = @id";
        cmd.Parameters.AddWithValue("@delta", delta);
        cmd.Parameters.AddWithValue("@id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ---------- 1 و 2: التقريب قبل التوازن، والرصيد بما يُحفظ في السطر ----------
    [Fact]
    public async Task Lines_are_rounded_before_balancing_and_the_account_balance_moves_by_the_saved_amount()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311");

        var je = await api.Post("/journalentries", Entry(("1111", 10.005m, 0), ("311", 0, 10.005m)));
        Assert.Equal(200, je.Status);
        var lines = je.Data!["lines"]!.AsArray();
        Assert.Equal(10.01m, lines.Single(l => l!["accountCode"].S() == "1111")!["debit"].D());
        Assert.Equal(10.01m, await BalanceAsync(api, "1111")); // لا 10.005: الرصيد = السطر المحفوظ
        Assert.True((await api.Get("/reports/BalanceIntegrity")).Data!["isConsistent"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Rounding_remainder_goes_to_the_largest_line_so_the_saved_entry_balances_exactly()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311"); await EquityAccountAsync(api, "312"); await EquityAccountAsync(api, "313");

        // متوازن قبل التقريب (100 = 33.333 + 33.333 + 33.334)، وبعد تقريب كل سطر 33.33 × 3 = 99.99
        var je = await api.Post("/journalentries", Entry(("1111", 100m, 0), ("311", 0, 33.333m), ("312", 0, 33.333m), ("313", 0, 33.334m)));
        Assert.Equal(200, je.Status);
        var credits = je.Data!["lines"]!.AsArray().Select(l => l!["credit"].D()).Where(c => c > 0).ToList();
        Assert.Equal(100m, credits.Sum());
        Assert.Equal(100m, je.Data["totalDebit"].D()); Assert.Equal(100m, je.Data["totalCredit"].D());
        Assert.All(credits, c => Assert.Equal(c, Math.Round(c, 2)));
        Assert.Equal(1, credits.Count(c => c == 33.34m));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task An_entry_unbalanced_beyond_rounding_is_still_rejected()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311");
        Assert.Equal(400, (await api.Post("/journalentries", Entry(("1111", 100m, 0), ("311", 0, 99.98m)))).Status);
    }

    // ---------- 1: كشف الانحراف وإصلاحه ----------
    [Fact]
    public async Task Drifted_balances_are_detected_and_rebuilt_from_entries_including_the_linked_customer()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311");
        var (customerId, customerAccount) = await SeedCustomerAsync(api);
        Assert.Equal(200, (await api.Post("/journalentries", Entry((customerAccount, 500m, 0), ("311", 0, 500m)))).Status);
        Assert.Equal(200, (await api.Post("/journalentries", Entry(("1111", 250m, 0), ("311", 0, 250m)))).Status);

        await CorruptBalanceAsync(api, "1111", 0.0037m);    // كسور تقريب قديمة
        await CorruptBalanceAsync(api, customerAccount, 277m);

        var check = (await api.Get("/reports/BalanceIntegrity")).Data!;
        Assert.False(check["isConsistent"]!.GetValue<bool>());
        Assert.Equal(2, check["driftedCount"]!.GetValue<int>());
        var cash = check["rows"]!.AsArray().Single(r => r!["accountCode"].S() == "1111")!;
        Assert.Equal(250.0037m, cash["storedBalance"].D()); Assert.Equal(250m, cash["ledgerBalance"].D()); Assert.Equal(0.0037m, cash["difference"].D());

        var rebuilt = await api.Post("/reports/BalanceIntegrity/rebuild");
        Assert.Equal(200, rebuilt.Status);
        Assert.True(rebuilt.Data!["rebuilt"]!.GetValue<bool>());
        Assert.True((await api.Get("/reports/BalanceIntegrity")).Data!["isConsistent"]!.GetValue<bool>());
        Assert.Equal(250m, await BalanceAsync(api, "1111"));
        Assert.Equal(500m, await BalanceAsync(api, customerAccount));
        Assert.Equal(500m, (await api.Get($"/customers/{customerId}")).Data!["currentBalance"].D());

        // بلا فروق: لا شيء يُعاد
        Assert.False((await api.Post("/reports/BalanceIntegrity/rebuild")).Data!["rebuilt"]!.GetValue<bool>());
    }

    // ---------- 3: الرصيد بإشارته في أرصدة الحسابات ----------
    [Fact]
    public async Task An_overdrawn_cash_account_shows_in_the_credit_column()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311");
        Assert.Equal(200, (await api.Post("/journalentries", Entry(("311", 300m, 0), ("1111", 0, 300m)))).Status); // مدين الطبيعة صار سالباً

        foreach (var query in new[] { "/reports/AccountBalances", "/reports/AccountBalances?module=accounting" })
        {
            var cash = (await api.Get(query)).Data!.AsArray().Single(r => r!["code"].S() == "1111")!;
            Assert.Equal(0m, cash["debit"].D());
            Assert.Equal(300m, cash["credit"].D());
            var equity = (await api.Get(query)).Data!.AsArray().Single(r => r!["code"].S() == "311")!;
            Assert.Equal(300m, equity["debit"].D()); // دائن الطبيعة صار مديناً
            Assert.Equal(0m, equity["credit"].D());
        }
    }

    // ---------- 4: الإلغاء بقيد عكسي بدل الحذف ----------
    [Fact]
    public async Task Deleting_a_manual_entry_cancels_it_with_a_reversal_and_keeps_both_on_record()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311");
        var first = await api.Post("/journalentries", Entry(("1111", 1000m, 0), ("311", 0, 1000m)));
        var id = first.Data!["id"].S();

        Assert.Equal(200, (await api.Delete($"/journalentries/{id}")).Status);

        var original = await api.Get($"/journalentries/{id}");
        Assert.Equal(200, original.Status); // لم يُمسح
        Assert.Equal("reversed", original.Data!["status"].S());
        Assert.Equal(0m, await BalanceAsync(api, "1111"));

        var all = (await api.Get("/journalentries?pageSize=50")).Data!["items"]!.AsArray();
        var reversal = all.Single(e => e!["sourceType"].S() == "reversal")!;
        Assert.Equal(id, reversal["sourceReferenceId"].S());

        // الترقيم متصل: الأصل ثم العكسي ثم التالي، بلا فجوة
        var next = await api.Post("/journalentries", Entry(("1111", 5m, 0), ("311", 0, 5m)));
        var numbers = new[] { first.Data["entryNumber"].S(), reversal["entryNumber"].S(), next.Data!["entryNumber"].S() }
            .Select(n => int.Parse(new string(n.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(new[] { numbers[0], numbers[0] + 1, numbers[0] + 2 }, numbers);

        Assert.Equal(409, (await api.Delete($"/journalentries/{id}")).Status); // الملغى لا يُلغى مرتين
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- 5: تاريخ الأعمال بتوقيت المملكة ----------
    [Fact]
    public void Business_date_is_saudi_time_not_utc()
    {
        // 01:00 يوم 1 أكتوبر في الرياض = 22:00 يوم 30 سبتمبر بالتوقيت العالمي
        Assert.Equal(new DateTime(2026, 10, 1, 1, 0, 0), SaudiTime.FromUtc(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateTime(2026, 10, 1), SaudiTime.FromUtc(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)).Date);
        var expected = DateTime.UtcNow.AddHours(3).Date;
        Assert.Equal(expected, SaudiTime.Today);
    }

    [Fact]
    public async Task An_entry_without_a_date_is_dated_today_in_saudi_time()
    {
        var api = await NewTenantAsync();
        await EquityAccountAsync(api, "311");
        var je = await api.Post("/journalentries", Entry(("1111", 10m, 0), ("311", 0, 10m)));
        var date = DateTime.Parse(je.Data!["date"].S(), CultureInfo.InvariantCulture);
        Assert.Equal(SaudiTime.Today, date);
    }

    // ---------- 6: مطابقة المخزون مع الأستاذ ----------
    [Fact]
    public async Task Item_inventory_reconciles_with_the_ledger_through_purchases_and_sales()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40); // افتتاحي بقيد مقابل الأرصدة الافتتاحية
        Assert.Equal(201, (await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 3, unitPrice = 100, vatRate = 15 } } })).Status);
        Assert.Equal(201, (await api.Post("/invoices", new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 5, unitPrice = 46, vatRate = 15 } } })).Status);

        var rec = (await api.Get("/reports/InventoryReconciliation")).Data!;
        var items = rec["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.True(items["isReconciled"]!.GetValue<bool>(), items.ToJsonString());
        Assert.Equal(items["subledgerValue"].D(), items["ledgerValue"].D());
        Assert.True(items["ledgerValue"].D() > 0);
        Assert.Contains("1141", items["accountCodes"]!.AsArray().Select(c => c.S()));
    }

    [Fact]
    public async Task A_stock_change_without_an_entry_shows_as_a_difference()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        // تسوية مباشرة في قاعدة البيانات خارج المحرك (كما في بيانات مستوردة): مخزون بلا قيد
        await using (var conn = new SqlConnection(Factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Product SET CurrentStock = CurrentStock + 2 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", product);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        var rec = (await api.Get("/reports/InventoryReconciliation")).Data!;
        Assert.False(rec["isReconciled"]!.GetValue<bool>());
        var items = rec["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.Equal(480m, items["subledgerValue"].D());
        Assert.Equal(400m, items["ledgerValue"].D());
        Assert.Equal(80m, items["difference"].D());
    }

    [Fact]
    public async Task Vehicle_inventory_row_compares_cars_in_stock_with_the_vehicle_inventory_account()
    {
        var api = await NewTenantWithModulesAsync("accounting", "car_showroom");
        var car = await api.Post("/vehicles", new
        {
            chassisNumber = Client.NewVin('7'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new", mileageKm = 0,
            purchasePrice = 40000, sellingPrice = 50000, vatMode = "standard_15", fuelType = "petrol", transmission = "automatic",
            colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);

        // بطاقة سيارة أُدخلت مباشرة بلا فاتورة شراء ولا قيد افتتاحي: تظهر فرقاً في المطابقة
        var vehicles = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "vehicles")!;
        Assert.Equal(40000m, vehicles["subledgerValue"].D());
        Assert.False(vehicles["isReconciled"]!.GetValue<bool>());
        Assert.Contains("1142", vehicles["accountCodes"]!.AsArray().Select(c => c.S()));
    }
}
