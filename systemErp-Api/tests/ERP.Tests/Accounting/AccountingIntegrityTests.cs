using Microsoft.AspNetCore.Hosting;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// سلامة الدفاتر: إقفال الفترات والسنة المالية، مشتريات الخدمات وفروق المرتجع، العملة الأجنبية،
/// تصنيف الإقرار الضريبي، وحماية الأرصدة والمخزون من العمليات المتزامنة.
/// </summary>
[Collection("api")]
public class AccountingIntegrityTests : TestBase
{
    public AccountingIntegrityTests(ErpFactory f) : base(f) { }

    private static object ServiceSale(DateTime? issueDate = null, decimal price = 100, string currency = "SAR", decimal rate = 1)
        => new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", issueDate = issueDate ?? DateTime.UtcNow, currencyCode = currency, exchangeRate = rate,
            items = new[] { new { itemName = "خدمة استشارية", quantity = 1, unitPrice = price, vatRate = 15 } },
        };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    // ---------- إقفال الفترات ----------
    [Fact]
    public async Task Locked_period_rejects_invoices_journal_entries_and_stock_movements_dated_inside_it()
    {
        var api = await NewTenantAsync();
        api.Language = "en";
        var product = await SeedProductAsync(api);
        var lockDate = DateTime.UtcNow.Date.AddDays(-10);
        var inside = lockDate.AddDays(-5);

        Assert.Equal(400, (await api.Put("/fiscalperiods/lock", new { lockedThrough = DateTime.UtcNow.Date.AddDays(3) })).Status); // لا إقفال للمستقبل
        var locked = await api.Put("/fiscalperiods/lock", new { lockedThrough = lockDate });
        Assert.Equal(200, locked.Status);
        Assert.StartsWith(lockDate.ToString("yyyy-MM-dd"), locked.Data!["booksLockedThrough"].S());

        var rejected = await api.Post("/invoices", ServiceSale(inside));
        Assert.Equal(409, rejected.Status);
        Assert.Contains("locked period", rejected.Body!["message"].S());
        Assert.Equal(409, (await api.Post("/invoices", ServiceSale(lockDate))).Status); // تاريخ الإقفال نفسه مقفل
        Assert.Equal(409, (await api.Post("/journalentries", new { date = inside, description = "x",
            lines = new[] { new { accountCode = "1111", debit = 10m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 10m } } })).Status);
        Assert.Equal(409, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 40, date = inside })).Status);
        Assert.Equal(0, await BalanceAsync(api, "1111"));

        // بعد تاريخ الإقفال يُرحَّل، وفتح الفترة يعيد السماح
        Assert.Equal(201, (await api.Post("/invoices", ServiceSale(lockDate.AddDays(1)))).Status);
        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = (DateTime?)null })).Status);
        Assert.Equal(201, (await api.Post("/invoices", ServiceSale(inside))).Status);
        Assert.Contains("PERIOD_LOCK_CHANGED", (await api.Get("/auditlogs?entityName=Tenant")).Data!["items"]!.AsArray().Select(x => x!["action"].S()));
    }

    [Fact]
    public async Task Closing_a_fiscal_year_moves_its_profit_to_retained_earnings_and_locks_it()
    {
        var api = await NewTenantAsync();
        var lastYear = DateTime.UtcNow.Year - 1;
        Assert.Equal(201, (await api.Post("/invoices", ServiceSale(new DateTime(lastYear, 3, 10), price: 1000))).Status); // إيراد 1000
        Assert.Equal(200, (await api.Post("/journalentries", new { date = new DateTime(lastYear, 6, 1), description = "إيجار",
            lines = new[] { new { accountCode = "521", debit = 300m, credit = 0m }, new { accountCode = "1111", debit = 0m, credit = 300m } } })).Status);
        Assert.Equal(201, (await api.Post("/invoices", ServiceSale(price: 50))).Status); // السنة الحالية لا تدخل الإقفال

        Assert.Equal(400, (await api.Post("/fiscalperiods/close-year", new { year = DateTime.UtcNow.Year })).Status); // لم تنتهِ
        var closed = await api.Post("/fiscalperiods/close-year", new { year = lastYear });
        Assert.Equal(200, closed.Status);
        Assert.Equal(1000, closed.Data!["totalRevenues"].D()); Assert.Equal(300, closed.Data["totalExpenses"].D()); Assert.Equal(700, closed.Data["netProfit"].D());
        Assert.Equal(700, await BalanceAsync(api, "32"));
        Assert.Equal(50, await BalanceAsync(api, "411"));  // بقي إيراد السنة الحالية فقط
        Assert.Equal(0, await BalanceAsync(api, "521"));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        // السنة مقفلة: لا إقفال ثانٍ، لا ترحيل فيها، ولا فتح لها دون عكس قيد الإقفال
        Assert.Equal(409, (await api.Post("/fiscalperiods/close-year", new { year = lastYear })).Status);
        Assert.Equal(409, (await api.Post("/invoices", ServiceSale(new DateTime(lastYear, 12, 31)))).Status);
        Assert.Equal(409, (await api.Put("/fiscalperiods/lock", new { lockedThrough = (DateTime?)null })).Status);
        var status = (await api.Get("/fiscalperiods")).Data!;
        Assert.Equal(lastYear, Assert.Single(status["closedYears"]!.AsArray())!.GetValue<int>());
        Assert.StartsWith($"{lastYear}-12-31", status["booksLockedThrough"].S());
    }

    // ---------- المشتريات ----------
    [Fact]
    public async Task Purchased_services_are_expensed_and_only_stock_items_reach_the_inventory_account()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        var purchase = await api.Post("/invoices", new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash",
            items = new object[]
            {
                new { itemId = product, quantity = 5, unitPrice = 40, vatRate = 15 },
                new { itemName = "أجور شحن", quantity = 1, unitPrice = 100, vatRate = 15 },
            },
        });
        Assert.Equal(201, purchase.Status);
        Assert.Equal(200, await BalanceAsync(api, "1141")); // الأصناف فقط
        Assert.Equal(100, await BalanceAsync(api, "524"));  // الخدمة مصروف
        Assert.Equal(45, await BalanceAsync(api, "1131"));
        Assert.Equal(-345, await BalanceAsync(api, "1111"));
        Assert.Equal(40, (await api.Get($"/products/{product}")).Data!["averageCost"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Purchase_return_leaves_inventory_at_cost_and_books_the_price_difference_as_variance()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        object Purchase(decimal price) => new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 10, unitPrice = price, vatRate = 15 } } };
        Assert.Equal(201, (await api.Post("/invoices", Purchase(40))).Status);
        var second = await api.Post("/invoices", Purchase(50)); // المتوسط 45
        Assert.Equal(201, second.Status);

        var ret = await api.Post("/invoices/returns", new { originalInvoiceId = second.Data!["id"].S(), returnReason = "تالف", lines = new[] { new { itemId = product, quantity = 4 } } });
        Assert.Equal(200, ret.Status);
        Assert.Equal(200, ret.Data!["subtotal"].D()); // يُسترد بسعر الشراء 50

        // حساب المخزون = قيمة المخزون الفعلية (16 × 45)، وفرق السعر 20 في حساب مستقل
        var stock = (await api.Get($"/products/{product}")).Data!;
        Assert.Equal(16, stock["currentStock"].D()); Assert.Equal(45, stock["averageCost"].D());
        Assert.Equal(720, await BalanceAsync(api, "1141"));
        Assert.Equal(-20, await BalanceAsync(api, "514"));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- العملة الأجنبية والإقرار الضريبي ----------
    [Fact]
    public async Task Foreign_currency_invoice_is_posted_in_the_base_currency_at_its_exchange_rate()
    {
        var api = await NewTenantAsync();
        var usd = await api.Post("/invoices", ServiceSale(price: 100, currency: "USD", rate: 3.75m)); // 115 دولار
        Assert.Equal(201, usd.Status);
        Assert.Equal(115, usd.Data!["grandTotal"].D()); // المستند بعملته
        Assert.Equal(431.25m, await BalanceAsync(api, "1111"));
        Assert.Equal(375, await BalanceAsync(api, "411"));
        Assert.Equal(56.25m, await BalanceAsync(api, "213"));

        // العملة الأساسية لا تُحوَّل ولو أُرسل سعر صرف
        var sar = await api.Post("/invoices", ServiceSale(price: 100, currency: "SAR", rate: 4));
        Assert.Equal(1, sar.Data!["exchangeRate"].D());
        Assert.Equal(475, await BalanceAsync(api, "411"));

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(475, vat["standardRatedSales"].D()); Assert.Equal(71.25m, vat["outputVat"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Qr_carries_the_saudi_issue_time_converted_to_utc()
    {
        var api = await NewTenantAsync();
        // 02:00 بتوقيت المملكة يوم 5 يناير = 23:00 بالتوقيت العالمي يوم 4 يناير
        var invoice = await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", issueDate = "2026-01-05", issueTime = "02:00:00",
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(201, invoice.Status);
        var tlv = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(invoice.Data!["zatcaQrCode"].S()));
        Assert.Contains("2026-01-04T23:00:00Z", tlv);
    }

    [Fact]
    public async Task Vat_return_includes_manual_journal_adjustments_but_not_the_settlement_with_the_authority()
    {
        var api = await NewTenantAsync();
        object Entry(string debit, string credit, decimal amount) => new { date = DateTime.UtcNow, description = "x",
            lines = new[] { new { accountCode = debit, debit = amount, credit = 0m }, new { accountCode = credit, debit = 0m, credit = amount } } };
        Assert.Equal(200, (await api.Post("/journalentries", Entry("522", "213", 40))).Status);   // تسوية تزيد ضريبة المخرجات
        Assert.Equal(200, (await api.Post("/journalentries", Entry("1131", "31", 10))).Status);   // تسوية تزيد ضريبة المدخلات
        Assert.Equal(200, (await api.Post("/journalentries", Entry("213", "1111", 25))).Status);  // سداد للهيئة: ليس تسوية
        var undone = (await api.Post("/journalentries", Entry("522", "213", 7))).Data!["id"].S();
        Assert.Equal(200, (await api.Post($"/journalentries/{undone}/reverse")).Status);          // تسوية معكوسة: أثرها صفر

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(40, vat["outputVatAdjustments"].D()); Assert.Equal(10, vat["inputVatAdjustments"].D());
        Assert.Equal(40, vat["outputVat"].D()); Assert.Equal(10, vat["inputVat"].D());
        Assert.Equal(30, vat["netVatPayable"].D());
    }

    [Fact]
    public async Task Vat_return_separates_standard_zero_rated_exempt_and_out_of_scope_lines_net_of_returns()
    {
        var api = await NewTenantAsync();
        object Line(string name, decimal price, decimal rate, string? category = null, string? reason = null) => new { itemName = name, quantity = 1, unitPrice = price, vatRate = rate, vatCategory = category, vatExemptionReasonCode = reason };
        object Sale(params object[] items) => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items };

        Assert.Equal(400, (await api.Post("/invoices", Sale(Line("معفى بضريبة", 30, 15, "exempt")))).Status); // غير الأساسي لا يحمل ضريبة
        Assert.Equal(400, (await api.Post("/invoices", Sale(Line("معفى بلا سبب", 30, 0, "exempt")))).Status); // التصنيف غير الأساسي يحمل سببه
        Assert.Equal(400, (await api.Post("/invoices", Sale(Line("سبب لا يطابق", 30, 0, "exempt", "VATEX-SA-32")))).Status);
        Assert.Equal(400, (await api.Post("/invoices", Sale(Line("رمز مجهول", 30, 0, "exempt", "VATEX-XX")))).Status);
        var sale = await api.Post("/invoices", Sale(Line("أساسي", 100, 15), Line("صفري محلي", 50, 0, "zero_rated", "VATEX-SA-35"), Line("تصدير", 70, 0, reason: "VATEX-SA-32"),
            Line("معفى", 30, 0, "exempt", "VATEX-SA-29"), Line("خارج النطاق", 20, 0, "out_of_scope", "VATEX-SA-OOS")));
        Assert.Equal(201, sale.Status);
        Assert.Equal(new[] { "standard", "zero_rated", "zero_rated", "exempt", "out_of_scope" }, sale.Data!["items"]!.AsArray().Select(i => i!["vatCategory"].S()));
        Assert.Equal("VATEX-SA-32", sale.Data["items"]![2]!["vatExemptionReasonCode"].S());
        Assert.Equal(201, (await api.Post("/invoices", new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", items = new[] { Line("إيجار", 200, 15) } })).Status);
        // مرتجع كامل لفاتورة أساسية أخرى يُطرح من الإقرار
        var other = await api.Post("/invoices", Sale(Line("أساسي", 400, 15)));
        Assert.Equal(200, (await api.Post("/invoices/returns", new { originalInvoiceId = other.Data!["id"].S(), returnReason = "x" })).Status);

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(100, vat["standardRatedSales"].D()); Assert.Equal(50, vat["zeroRatedSales"].D()); Assert.Equal(70, vat["exportSales"].D());
        Assert.Equal(30, vat["exemptSales"].D()); Assert.Equal(20, vat["outOfScopeSales"].D());
        Assert.Equal(15, vat["outputVat"].D());
        Assert.Equal(200, vat["standardRatedPurchases"].D()); Assert.Equal(30, vat["inputVat"].D());
        Assert.Equal(-15, vat["netVatPayable"].D());
    }

    // ---------- التزامن ----------
    [Fact]
    public async Task Concurrent_sales_never_lose_a_balance_update_or_oversell_stock()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 3);
        object Sale() => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } };

        // ست عمليات بيع متزامنة على مخزون 3: الناجح منها لا يتجاوز الرصيد، وكل ناجح ظهر أثره كاملاً
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.Post("/invoices", Sale())));
        var sold = results.Count(r => r.Status == 201);
        Assert.InRange(sold, 1, 3);
        Assert.All(results.Where(r => r.Status != 201), r => Assert.Equal(409, r.Status));
        Assert.Equal(3 - sold, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(sold * 115, await BalanceAsync(api, "1111"));
        Assert.Equal(sold * 100, await BalanceAsync(api, "411"));
        Assert.Equal(sold, results.Where(r => r.Status == 201).Select(r => r.Data!["invoiceNumber"].S()).Distinct().Count());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- الأمان ----------
    [Fact]
    public async Task Public_auth_endpoints_are_rate_limited()
    {
        using var limited = Factory.WithWebHostBuilder(b => b.UseSetting("Auth:RateLimitPerMinute", "3"));
        var anonymous = new Client(limited.CreateClient());
        var statuses = new List<int>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await anonymous.SendAsync(HttpMethod.Post, "/auth/login", new { email = "nobody@test.com", password = "wrong-password" }, anonymous: true)).Status);
        Assert.Equal(new[] { 401, 401, 401, 429, 429 }, statuses);
    }
}
