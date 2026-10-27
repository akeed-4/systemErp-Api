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

        var rejected = await api.PostInvoice(ServiceSale(inside));
        Assert.Equal(409, rejected.Status);
        Assert.Contains("locked period", rejected.Body!["message"].S());
        Assert.Equal(409, (await api.PostInvoice(ServiceSale(lockDate))).Status); // تاريخ الإقفال نفسه مقفل
        Assert.Equal(409, (await api.Post("/journalentries", new { date = inside, description = "x",
            lines = new[] { new { accountCode = "1111", debit = 10m, credit = 0m }, new { accountCode = "31", debit = 0m, credit = 10m } } })).Status);
        Assert.Equal(409, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 40, date = inside })).Status);
        Assert.Equal(0, await BalanceAsync(api, "1111"));

        // بعد تاريخ الإقفال يُرحَّل، وفتح الفترة يعيد السماح
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(lockDate.AddDays(1)))).Status);
        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = (DateTime?)null })).Status);
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(inside))).Status);
        Assert.Contains("PERIOD_LOCK_CHANGED", (await api.Get("/auditlogs?entityName=Tenant")).Data!["items"]!.AsArray().Select(x => x!["action"].S()));
    }

    [Fact]
    public async Task Documents_in_a_locked_period_cannot_be_edited_or_deleted_so_its_vat_return_stays_as_filed()
    {
        var api = await NewTenantAsync();
        var date = DateTime.UtcNow.Date.AddDays(-20);
        object Payment(decimal amount, decimal vat, DateTime on) => new { type = "payment", date = on, amount, vatAmount = vat, partyName = "مصروف كهرباء",
            partyAccountCode = "524", treasuryAccountCode = "1111", paymentMethod = "cash" };
        var voucher = await api.Post("/vouchers", Payment(115, 15, date));
        Assert.True(voucher.Success, voucher.Body?.ToJsonString());
        var id = voucher.Data!["id"].S();
        string Period(DateTime d) => $"from={d.AddDays(-1):yyyy-MM-dd}&to={d.AddDays(1):yyyy-MM-dd}";
        Assert.Equal(15, (await api.Get($"/reports/VatReturn?{Period(date)}")).Data!["inputVat"].D());

        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = date.AddDays(5) })).Status); // قُدِّم إقرار الفترة وأُقفلت

        // لا تعديل (ولو بنقله إلى فترة مفتوحة) ولا حذف: الإقرار المقدَّم يبقى كما هو
        Assert.Equal(409, (await api.Put($"/vouchers/{id}", Payment(230, 30, date))).Status);
        Assert.Equal(409, (await api.Put($"/vouchers/{id}", Payment(115, 15, DateTime.UtcNow.Date))).Status);
        Assert.Equal(409, (await api.Delete($"/vouchers/{id}")).Status);
        Assert.Equal(15, (await api.Get($"/reports/VatReturn?{Period(date)}")).Data!["inputVat"].D());
        Assert.Equal(-115, await BalanceAsync(api, "1111"));
    }

    [Fact]
    public async Task Closing_a_fiscal_year_moves_its_profit_to_retained_earnings_and_locks_it()
    {
        var api = await NewTenantAsync();
        var lastYear = DateTime.UtcNow.Year - 1;
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(new DateTime(lastYear, 3, 10), price: 1000))).Status); // إيراد 1000
        Assert.Equal(200, (await api.Post("/journalentries", new { date = new DateTime(lastYear, 6, 1), description = "إيجار",
            lines = new[] { new { accountCode = "521", debit = 300m, credit = 0m }, new { accountCode = "1111", debit = 0m, credit = 300m } } })).Status);
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(price: 50))).Status); // السنة الحالية لا تدخل الإقفال

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
        Assert.Equal(409, (await api.PostInvoice(ServiceSale(new DateTime(lastYear, 12, 31)))).Status);
        Assert.Equal(409, (await api.Put("/fiscalperiods/lock", new { lockedThrough = (DateTime?)null })).Status);
        var status = (await api.Get("/fiscalperiods")).Data!;
        Assert.Equal(lastYear, Assert.Single(status["closedYears"]!.AsArray())!.GetValue<int>());
        Assert.StartsWith($"{lastYear}-12-31", status["booksLockedThrough"].S());
    }

    [Fact]
    public async Task Reversing_a_closing_entry_reopens_its_year_without_touching_the_current_year()
    {
        var api = await NewTenantAsync();
        var (older, lastYear) = (DateTime.UtcNow.Year - 2, DateTime.UtcNow.Year - 1);
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(new DateTime(older, 5, 1), price: 200))).Status);
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(new DateTime(lastYear, 3, 10), price: 1000))).Status);
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(price: 50))).Status);
        var olderClosing = (await api.Post("/fiscalperiods/close-year", new { year = older })).Data!["journalEntryId"].S();
        var closing = (await api.Post("/fiscalperiods/close-year", new { year = lastYear })).Data!["journalEntryId"].S();

        // السنة الأقدم لا تُفتح والأحدث مقفلة فوقها
        Assert.Equal(409, (await api.Post($"/journalentries/{olderClosing}/reverse")).Status);

        var reversal = await api.Post($"/journalentries/{closing}/reverse");
        Assert.Equal(200, reversal.Status);
        Assert.StartsWith($"{lastYear}-12-31", reversal.Data!["date"].S()); // في السنة المُعاد فتحها لا بتاريخ اليوم
        Assert.Equal(1050, await BalanceAsync(api, "411"));
        Assert.Equal(200, await BalanceAsync(api, "32")); // تبقى نتيجة السنة الأقدم المقفلة فقط

        var status = (await api.Get("/fiscalperiods")).Data!;
        Assert.Equal(older, Assert.Single(status["closedYears"]!.AsArray())!.GetValue<int>());
        Assert.StartsWith($"{older}-12-31", status["booksLockedThrough"].S());
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(new DateTime(lastYear, 12, 31)))).Status); // السنة مفتوحة للتصحيح

        // قائمة دخل السنة المُعاد فتحها: إيرادها مرة واحدة (عكس قيد الإقفال لا يُحسب إيراداً ثانياً) + التصحيح
        var reopened = (await api.Get($"/financialstatements/IncomeStatement?from={lastYear}-01-01&to={lastYear}-12-31")).Data!;
        Assert.Equal(1100, reopened["revenue"]!["total"].D());

        // قائمة دخل السنة الحالية لا ترث إيرادات السنة المُعاد فتحها
        var from = new DateTime(DateTime.UtcNow.Year, 1, 1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        Assert.Equal(50, (await api.Get($"/financialstatements/IncomeStatement?from={from}&to={to}")).Data!["revenue"]!["total"].D());
        Assert.Contains("FISCAL_YEAR_REOPENED", (await api.Get("/auditlogs?entityName=Tenant")).Data!["items"]!.AsArray().Select(x => x!["action"].S()));

        // زر «إلغاء القيد» في شاشة القيود يعيد فتح السنة بالطريقة نفسها
        Assert.Equal(200, (await api.Delete($"/journalentries/{olderClosing}")).Status);
        status = (await api.Get("/fiscalperiods")).Data!;
        Assert.Empty(status["closedYears"]!.AsArray());
        Assert.Null(status["booksLockedThrough"]);
        Assert.Equal(0, await BalanceAsync(api, "32"));
        Assert.Equal(1350, await BalanceAsync(api, "411")); // 200 + 1000 + 50 + تصحيح 100 في السنة المُعاد فتحها
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- المشتريات ----------
    [Fact]
    public async Task Purchased_services_are_expensed_and_only_stock_items_reach_the_inventory_account()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 0);
        var purchase = await api.PostInvoice(new
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
        Assert.Equal(201, (await api.PostInvoice(Purchase(40))).Status);
        var second = await api.PostInvoice(Purchase(50)); // المتوسط 45
        Assert.Equal(201, second.Status);

        var ret = await api.PostReturn(new { originalInvoiceId = second.Data!["id"].S(), returnReason = "تالف", lines = new[] { new { itemId = product, quantity = 4 } } });
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
        var usd = await api.PostInvoice(ServiceSale(price: 100, currency: "USD", rate: 3.75m)); // 115 دولار
        Assert.Equal(201, usd.Status);
        Assert.Equal(115, usd.Data!["grandTotal"].D()); // المستند بعملته
        Assert.Equal(431.25m, await BalanceAsync(api, "1111"));
        Assert.Equal(375, await BalanceAsync(api, "411"));
        Assert.Equal(56.25m, await BalanceAsync(api, "213"));

        // العملة الأساسية لا تُحوَّل ولو أُرسل سعر صرف
        var sar = await api.PostInvoice(ServiceSale(price: 100, currency: "SAR", rate: 4));
        Assert.Equal(1, sar.Data!["exchangeRate"].D());
        Assert.Equal(475, await BalanceAsync(api, "411"));

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(475, vat["standardRatedSales"].D()); Assert.Equal(71.25m, vat["outputVat"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Invoice_posts_to_the_account_of_the_chosen_configured_payment_method()
    {
        var api = await NewTenantAsync();
        // طريقة «تحويل» مؤسَّسة مرتبطة بالصندوق 1111: اختيارها يُرحَّل عليه لا على البنك الافتراضي للنوع (1112)
        Assert.Equal(201, (await api.Post("/paymentmethods", new { code = "TR-BOX", nameAr = "تحويل للصندوق", nameEn = "Transfer to box", type = "cheque", linkedAccountCode = "1111", status = "active" })).Status);
        var options = (await api.Get("/lookups/payment-methods")).Data!.AsArray();
        Assert.Contains(options, m => m!["code"].S() == "TR-BOX");

        object Sale(string? code) => new { kind = "sales", invoiceType = "simplified", status = "posted", paymentMethod = "bank_transfer", paymentMethodCode = code,
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } } };
        var chosen = await api.PostInvoice(Sale("TR-BOX"));
        Assert.Equal(201, chosen.Status);
        Assert.Equal("TR-BOX", chosen.Data!["paymentMethodCode"].S());
        Assert.Equal(115, await BalanceAsync(api, "1111"));
        Assert.Equal(0, await BalanceAsync(api, "1112"));

        // بلا رمز: السلوك السابق (حساب النوع الافتراضي)
        Assert.Equal(201, (await api.PostInvoice(Sale(null))).Status);
        Assert.Equal(115, await BalanceAsync(api, "1112"));

        // الدفع المتعدد: كل جزء على حساب طريقته
        var split = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", status = "posted", paymentMethod = "cash", isSplitPayment = true,
            paymentSplits = new object[] { new { method = "bank_transfer", paymentMethodCode = "TR-BOX", amount = 15 }, new { method = "bank_transfer", amount = 100 } },
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } } });
        Assert.Equal(201, split.Status);
        Assert.Equal(130, await BalanceAsync(api, "1111"));
        Assert.Equal(215, await BalanceAsync(api, "1112"));
    }

    [Fact]
    public async Task Qr_carries_the_saudi_issue_time_converted_to_utc()
    {
        var api = await NewTenantAsync();
        // 02:00 بتوقيت المملكة يوم 5 يناير = 23:00 بالتوقيت العالمي يوم 4 يناير
        var invoice = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", issueDate = "2026-01-05", issueTime = "02:00:00",
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

        Assert.Equal(400, (await api.PostInvoice(Sale(Line("معفى بضريبة", 30, 15, "exempt")))).Status); // غير الأساسي لا يحمل ضريبة
        Assert.Equal(400, (await api.PostInvoice(Sale(Line("معفى بلا سبب", 30, 0, "exempt")))).Status); // التصنيف غير الأساسي يحمل سببه
        Assert.Equal(400, (await api.PostInvoice(Sale(Line("سبب لا يطابق", 30, 0, "exempt", "VATEX-SA-32")))).Status);
        Assert.Equal(400, (await api.PostInvoice(Sale(Line("رمز مجهول", 30, 0, "exempt", "VATEX-XX")))).Status);
        var sale = await api.PostInvoice(Sale(Line("أساسي", 100, 15), Line("صفري محلي", 50, 0, "zero_rated", "VATEX-SA-35"), Line("تصدير", 70, 0, reason: "VATEX-SA-32"),
            Line("معفى", 30, 0, "exempt", "VATEX-SA-29"), Line("خارج النطاق", 20, 0, "out_of_scope", "VATEX-SA-OOS")));
        Assert.Equal(201, sale.Status);
        Assert.Equal(new[] { "standard", "zero_rated", "zero_rated", "exempt", "out_of_scope" }, sale.Data!["items"]!.AsArray().Select(i => i!["vatCategory"].S()));
        Assert.Equal("VATEX-SA-32", sale.Data["items"]![2]!["vatExemptionReasonCode"].S());
        Assert.Equal(201, (await api.PostInvoice(new { kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", items = new[] { Line("إيجار", 200, 15) } })).Status);
        // مرتجع كامل لفاتورة أساسية أخرى يُطرح من الإقرار
        var other = await api.PostInvoice(Sale(Line("أساسي", 400, 15)));
        Assert.Equal(200, (await api.PostReturn(new { originalInvoiceId = other.Data!["id"].S(), returnReason = "x" })).Status);

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(100, vat["standardRatedSales"].D()); Assert.Equal(50, vat["zeroRatedSales"].D()); Assert.Equal(70, vat["exportSales"].D());
        Assert.Equal(30, vat["exemptSales"].D()); Assert.Equal(20, vat["outOfScopeSales"].D());
        Assert.Equal(15, vat["outputVat"].D());
        Assert.Equal(200, vat["standardRatedPurchases"].D()); Assert.Equal(30, vat["inputVat"].D());
        Assert.Equal(-15, vat["netVatPayable"].D());
    }

    [Fact]
    public async Task Accounting_reports_are_scoped_to_the_requested_module()
    {
        var api = await NewTenantAsync();
        Assert.Equal(201, (await api.PostInvoice(ServiceSale(price: 100))).Status);
        var (supplier, _) = await SeedSupplierAsync(api);
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted",
            inventoryAccountCode = "1142",
            vehicleLines = new[]
            {
                new { vin = Client.NewVin('2'), brandNameAr = "تويوتا", modelNameAr = "كامري", trimNameAr = "GLE", year = 2025,
                    colorExterior = "أبيض", colorInterior = "بيج", vatMode = "standard_15", unitPrice = 1000m, discount = 0m },
            },
        });
        Assert.Equal(201, purchase.Status);

        var vehicle = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!;
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            vehicleLines = new[] { new { vehicleId = vehicle["id"].S(), unitPrice = 1500m, discount = 0m, vatMode = "standard_15" } },
        });
        Assert.Equal(201, sale.Status);

        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var accountingVat = (await api.Get($"/reports/VatReturn?from={from}&to={to}&module=accounting")).Data!;
        var showroomVat = (await api.Get($"/reports/VatReturn?from={from}&to={to}&module=car_showroom")).Data!;
        var combinedVat = (await api.Get($"/reports/VatReturn?from={from}&to={to}&module=accounting,car_showroom")).Data!;
        var allVat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(100, accountingVat["standardRatedSales"].D());
        Assert.Equal(0, accountingVat["standardRatedPurchases"].D());
        Assert.Equal(1500, showroomVat["standardRatedSales"].D());
        Assert.Equal(1000, showroomVat["standardRatedPurchases"].D());
        Assert.Equal(allVat["standardRatedSales"].D(), combinedVat["standardRatedSales"].D());
        Assert.Equal(allVat["standardRatedPurchases"].D(), combinedVat["standardRatedPurchases"].D());
        Assert.Equal(1600, allVat["standardRatedSales"].D());
        Assert.Equal(1000, allVat["standardRatedPurchases"].D());

        var accountingBalances = (await api.Get("/reports/AccountBalances?module=accounting")).Data!.AsArray();
        var showroomBalances = (await api.Get("/reports/AccountBalances?module=car_showroom")).Data!.AsArray();
        Assert.Contains(accountingBalances, row => row!["code"]!.S() == "411");
        Assert.DoesNotContain(accountingBalances, row => row!["code"]!.S() == "1142");
        Assert.Contains(showroomBalances, row => row!["code"]!.S() == "1142");
        Assert.DoesNotContain(showroomBalances, row => row!["code"]!.S() == "411");

        var accountingLedger = (await api.Get("/reports/JournalLedger?module=accounting")).Data!.AsArray();
        var showroomLedger = (await api.Get("/reports/JournalLedger?module=car_showroom")).Data!.AsArray();
        Assert.Contains(accountingLedger, row => row!["accountCode"]!.S() == "411");
        Assert.DoesNotContain(accountingLedger, row => row!["accountCode"]!.S() == "1142");
        Assert.Contains(showroomLedger, row => row!["accountCode"]!.S() == "1142");
        Assert.DoesNotContain(showroomLedger, row => row!["accountCode"]!.S() == "411");

        var accountingTrial = Totals(await api.Get("/reports/TrialBalance?module=accounting"));
        var showroomTrial = Totals(await api.Get("/reports/TrialBalance?module=car_showroom"));
        var combinedTrial = Totals(await api.Get("/reports/TrialBalance?module=accounting,car_showroom"));
        var allTrial = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(accountingTrial.Debit, accountingTrial.Credit);
        Assert.Equal(showroomTrial.Debit, showroomTrial.Credit);
        Assert.NotEqual(accountingTrial.Debit, showroomTrial.Debit);
        Assert.Equal(allTrial.Debit, combinedTrial.Debit);

        var accountingPosition = (await api.Get($"/reports/BalanceSheet?asOf={to}&module=accounting")).Data!;
        var showroomPosition = (await api.Get($"/reports/BalanceSheet?asOf={to}&module=car_showroom")).Data!;
        Assert.True(accountingPosition["isBalanced"]!.GetValue<bool>());
        Assert.True(showroomPosition["isBalanced"]!.GetValue<bool>());

        var income = (await api.Get($"/financialstatements/IncomeStatement?from={from}&to={to}&module=car_showroom")).Data!;
        Assert.Equal(1500, income["revenue"]!["total"].D());
        Assert.Equal(1000, income["costOfSales"]!["total"].D());
    }

    [Fact]
    public async Task Cash_purchase_from_a_known_supplier_moves_through_the_supplier_account()
    {
        var api = await NewTenantAsync();
        var (supplier, code) = await SeedSupplierAsync(api);
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", partyId = supplier, status = "posted",
            items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal(201, purchase.Status);

        var row = (await api.Get("/reports/TrialBalance?module=accounting")).Data!.AsArray().FirstOrDefault(r => r!["accountCode"]!.S() == code);
        Assert.NotNull(row);
        Assert.Equal(115, row!["periodCredit"].D()); // الفاتورة كاملة على المورد
        Assert.Equal(115, row["periodDebit"].D());   // والمدفوع نقداً سُدِّد من حسابه
        Assert.Equal(0, row["closingCredit"].D());
        Assert.Equal(0, await BalanceAsync(api, code));
    }

    [Fact]
    public async Task Party_opening_balances_appear_in_every_module_trial_balance()
    {
        var api = await NewTenantAsync();
        var supplier = await api.Post("/suppliers", new { nameAr = "مورد معرض", phone = "050", city = "جدة", status = "active", vatNumber = Client.NewVat(), openingBalance = 700 });
        var code = supplier.Data!["accountCode"].S();
        Assert.StartsWith("211", code);

        foreach (var module in new[] { "accounting", "car_showroom" })
        {
            var trial = await api.Get($"/reports/TrialBalance?module={module}");
            var row = trial.Data!.AsArray().FirstOrDefault(r => r!["accountCode"]!.S() == code);
            Assert.NotNull(row);
            Assert.Equal(700, row!["closingCredit"].D());
            var (d, c) = Totals(trial); Assert.Equal(d, c);
            var position = (await api.Get($"/reports/BalanceSheet?module={module}")).Data!;
            Assert.Contains(position["liabilities"]!.AsArray(), l => l!["accountCode"]!.S() == code);
            Assert.True(position["isBalanced"]!.GetValue<bool>());
        }

        // تعديل الافتتاحي يعكس قيده القديم: العكس يتبعه في كل وحدة فلا يُعدّ القديم مرتين
        var id = supplier.Data["id"].S();
        Assert.Equal(200, (await api.Put($"/suppliers/{id}", new { nameAr = "مورد معرض", phone = "050", city = "جدة", status = "active", vatNumber = supplier.Data["vatNumber"].S(), openingBalance = 400 })).Status);
        var showroom = await api.Get("/reports/TrialBalance?module=car_showroom");
        Assert.Equal(400, showroom.Data!.AsArray().First(r => r!["accountCode"]!.S() == code)!["closingCredit"].D());
    }

    // ---------- التزامن ----------
    [Fact]
    public async Task Concurrent_sales_never_lose_a_balance_update_or_oversell_stock()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 3);
        object Sale() => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } };

        // ست عمليات بيع متزامنة على مخزون 3: الناجح منها لا يتجاوز الرصيد، وكل ناجح ظهر أثره كاملاً
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => api.PostInvoice(Sale())));
        var sold = results.Count(r => r.Status == 201);
        Assert.InRange(sold, 1, 3);
        Assert.All(results.Where(r => r.Status != 201), r => Assert.Equal(409, r.Status));
        Assert.Equal(3 - sold, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(sold * 115, await BalanceAsync(api, "1111"));
        Assert.Equal(sold * 100, await BalanceAsync(api, "411"));
        Assert.Equal(sold, results.Where(r => r.Status == 201).Select(r => r.Data!["invoiceNumber"].S()).Distinct().Count());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Posting_the_same_draft_concurrently_posts_it_once()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var draft = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } } });
        var path = await api.InvoicePath(draft.Data!["id"].S(), "/post");
        var entriesBefore = (await api.Get("/journalentries")).Data!["totalCount"].D();

        // ضغطات اعتماد متزامنة على المسودة نفسها: واحدة تُرحِّل والباقي يُرفض، ولا يتكرر القيد أو صرف المخزون
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => api.Post(path)));
        Assert.Equal(1, results.Count(r => r.Status == 200));
        Assert.Equal("posted", (await api.Get(await api.InvoicePath(draft.Data["id"].S(), ""))).Data!["status"].S());
        Assert.Equal(8, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(230, await BalanceAsync(api, "1111"));
        Assert.Equal(200, await BalanceAsync(api, "411"));
        Assert.True((await api.Get("/journalentries")).Data!["totalCount"].D() - entriesBefore >= 1);
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
        // الطلبات الخاسرة تعارضٌ لا خطأ خادم
        Assert.All(results.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));
    }

    [Fact]
    public async Task Posting_the_same_purchase_draft_concurrently_posts_it_once()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (supplier, account) = await SeedSupplierAsync(api);
        var draft = await api.PostInvoice(new { kind = "purchase", invoiceType = "simplified", paymentMethod = "credit", partyId = supplier, status = "draft",
            items = new[] { new { itemId = product, quantity = 5, unitPrice = 40, vatRate = 15 } } });
        var path = await api.InvoicePath(draft.Data!["id"].S(), "/post");

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => api.Post(path)));
        Assert.Equal(1, results.Count(r => r.Status == 200));
        Assert.All(results.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));
        Assert.Equal(15, (await api.Get($"/products/{product}")).Data!["currentStock"].D()); // دخلت الكمية مرة واحدة
        Assert.Equal(230, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        Assert.Equal(230, await BalanceAsync(api, account));
        Assert.Equal(30, await BalanceAsync(api, "1131"));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Posting_different_drafts_concurrently_issues_unique_numbers_without_gaps()
    {
        var api = await NewTenantAsync();
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
            ids.Add((await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft",
                items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } } })).Data!["id"].S());

        // أول ترحيل في المنشأة: عدّاد الأرقام يُنشأ تحت التزامن أيضاً
        var results = await Task.WhenAll(ids.Select(async id => await api.Post(await api.InvoicePath(id, "/post"))));
        Assert.All(results, r => Assert.Equal(200, r.Status));
        var numbers = results.Select(r => long.Parse(r.Data!["invoiceNumber"].S()["SINV-".Length..])).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range((int)numbers[0], ids.Count).Select(n => (long)n), numbers);
        Assert.Equal(575, await BalanceAsync(api, "1111"));
    }

    [Fact]
    public async Task A_number_collision_while_posting_is_a_conflict_that_rolls_the_whole_posting_back()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        object Sale(string status, int quantity) => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status,
            items = new[] { new { itemId = product, quantity, unitPrice = 100, vatRate = 15 } } };
        var first = (await api.PostInvoice(Sale("posted", 1))).Data!["invoiceNumber"].S();
        var draft = await api.PostInvoice(Sale("draft", 2));
        var path = await api.InvoicePath(draft.Data!["id"].S(), "");
        var entriesBefore = (await api.Get("/journalentries")).Data!["totalCount"].D();

        // عدّاد أُرجع خطوة (استيراد/تعديل يدوي): الرقم التالي صُرف من قبل، والفهرس الفريد يمنع تكراره
        await ShiftSequenceAsync(api, "sales_invoice", -1);
        Assert.Equal(409, (await api.Post(path + "/post")).Status);

        // تراجعت المعاملة كلها: لا ترحيل ولا رقم ولا مخزون ولا قيد ولا نقدية
        var after = (await api.Get(path)).Data!;
        Assert.Equal("draft", after["status"].S());
        Assert.StartsWith("DRAFT-", after["invoiceNumber"].S());
        Assert.Equal(9, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(entriesBefore, (await api.Get("/journalentries")).Data!["totalCount"].D());
        Assert.Equal(115, await BalanceAsync(api, "1111"));

        // بعد تصحيح العدّاد تُرحَّل بالرقم التالي مباشرة (لم يستهلك الفشل رقماً)
        await ShiftSequenceAsync(api, "sales_invoice", 1);
        var posted = await api.Post(path + "/post");
        Assert.Equal(200, posted.Status);
        Assert.Equal(long.Parse(first["SINV-".Length..]) + 1, long.Parse(posted.Data!["invoiceNumber"].S()["SINV-".Length..]));
        Assert.Equal(7, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    private async Task ShiftSequenceAsync(Client api, string key, int delta)
    {
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE NumberSequence SET LastValue = LastValue + @delta WHERE [Key] = @key AND TenantId = @tenant";
        cmd.Parameters.AddWithValue("@delta", delta);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@tenant", api.TenantId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
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
