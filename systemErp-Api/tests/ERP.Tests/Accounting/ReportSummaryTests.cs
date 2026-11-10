using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// مؤشرات لوحة التحكم ودفتر اليومية العام وتوزيع مدفوعات المبيعات والبحث العام: كلها في SQL على كامل البيانات،
/// بصيغ محاسبية موثّقة، مع عزل المنشآت والصلاحيات. القيم المتوقعة تُحسب في C# من البيانات المزروعة.
/// </summary>
[Collection("api")]
public class ReportSummaryTests : TestBase
{
    public ReportSummaryTests(ErpFactory f) : base(f) { }

    private static string Esc(string s) => Uri.EscapeDataString(s);
    private static DateTime MonthStart(int monthsAgo) { var n = DateTime.UtcNow; return new DateTime(n.Year, n.Month, 1, 10, 0, 0).AddMonths(-monthsAgo); }

    private static Invoice Inv(string number, InvoiceKind kind, string status, DateTime date, decimal subtotal, decimal cost,
        PaymentMethod method = PaymentMethod.Cash) => new()
    {
        InvoiceNumber = number, Kind = kind, Status = status, IssueDate = date, InvoiceType = InvoiceType.Simplified,
        PartyName = "طرف", PaymentMethod = method,
        Subtotal = subtotal, VatTotal = subtotal * 0.15m, GrandTotal = subtotal * 1.15m, TotalCost = cost, GrossProfit = subtotal - cost,
        IsReturn = kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn,
    };

    // ---------------- لوحة التحكم ----------------

    [Fact]
    public async Task Dashboard_uses_posted_documents_net_of_returns_excluding_vat_over_more_than_1000_invoices()
    {
        var api = await NewTenantAsync();
        var docs = new List<Invoice>();
        // 1100 فاتورة بيع مرحّلة موزعة على آخر 11 شهرًا (100 لكل شهر)، ثلثها آجل
        for (var n = 1; n <= 1100; n++)
            docs.Add(Inv($"S-{n:D4}", InvoiceKind.Sales, "posted", MonthStart((n - 1) % 11).AddDays(n % 20), n, n * 0.6m, n % 3 == 0 ? PaymentMethod.Credit : PaymentMethod.Cash));
        // مسودات وملغاة: لا تدخل في أي مؤشر
        docs.Add(Inv("S-DRAFT", InvoiceKind.Sales, "draft", MonthStart(0), 99999, 1));
        docs.Add(Inv("S-CANCEL", InvoiceKind.Sales, "cancelled", MonthStart(0), 88888, 1));
        // مرتجعات مبيعات مرحّلة تُطرح
        for (var n = 1; n <= 30; n++) docs.Add(Inv($"SR-{n:D3}", InvoiceKind.SalesReturn, "posted", MonthStart(n % 3), 10, 6));
        // مشتريات: نقدية وآجلة، ومرتجع آجل
        for (var n = 1; n <= 40; n++) docs.Add(Inv($"P-{n:D3}", InvoiceKind.Purchase, "posted", MonthStart(n % 4), 100, 0, n % 2 == 0 ? PaymentMethod.Credit : PaymentMethod.BankTransfer));
        docs.Add(Inv("P-DRAFT", InvoiceKind.Purchase, "draft", MonthStart(0), 77777, 0));
        docs.Add(Inv("PR-1", InvoiceKind.PurchaseReturn, "posted", MonthStart(1), 50, 0, PaymentMethod.Credit));
        await SeedEntitiesAsync(api.TenantId, docs);
        await SeedEntitiesAsync(api.TenantId, new object[]
        {
            new Voucher { VoucherNumber = "V-1", Type = VoucherType.Receipt, Date = DateTime.UtcNow, Amount = 5, AmountInWordsAr = "-", PartyName = "x", PartyAccountCode = "112", TreasuryAccountCode = "1111" },
            new JournalEntry { EntryNumber = "JE-P", Date = DateTime.UtcNow, Description = "x", Status = JournalEntryStatus.Posted },
            new JournalEntry { EntryNumber = "JE-D", Date = DateTime.UtcNow, Description = "x", Status = JournalEntryStatus.Draft },
        });

        var posted = docs.Where(d => d.Status == "posted").ToList();
        decimal Sub(InvoiceKind k, Func<Invoice, bool>? f = null) => posted.Where(d => d.Kind == k && (f == null || f(d))).Sum(d => d.Subtotal);
        decimal Cost(InvoiceKind k) => posted.Where(d => d.Kind == k).Sum(d => d.TotalCost);
        decimal Vat(InvoiceKind k) => posted.Where(d => d.Kind == k).Sum(d => d.VatTotal);
        decimal Grand(InvoiceKind k) => posted.Where(d => d.Kind == k).Sum(d => d.GrandTotal);

        var r = await api.Get("/reports/DashboardSummary?months=9");
        Assert.Equal(200, r.Status);
        var d = r.Data!;
        var netSales = Sub(InvoiceKind.Sales) - Sub(InvoiceKind.SalesReturn);
        var netCost = Cost(InvoiceKind.Sales) - Cost(InvoiceKind.SalesReturn);
        Assert.Equal(netSales, d["netSales"].D());
        Assert.Equal(netCost, d["netCostOfSales"].D());
        Assert.Equal(netSales - netCost, d["grossProfit"].D());
        Assert.Equal(Vat(InvoiceKind.Sales) - Vat(InvoiceKind.SalesReturn), d["netOutputVat"].D());
        var netPurchases = Sub(InvoiceKind.Purchase) - Sub(InvoiceKind.PurchaseReturn);
        Assert.Equal(netPurchases, d["netPurchases"].D());
        Assert.Equal(Grand(InvoiceKind.Purchase) - Grand(InvoiceKind.PurchaseReturn), d["netPurchasesWithVat"].D());
        Assert.Equal(Vat(InvoiceKind.Purchase) - Vat(InvoiceKind.PurchaseReturn), d["netInputVat"].D());
        var netCredit = Sub(InvoiceKind.Purchase, x => x.PaymentMethod == PaymentMethod.Credit) - Sub(InvoiceKind.PurchaseReturn, x => x.PaymentMethod == PaymentMethod.Credit);
        Assert.Equal(netCredit, d["netCreditPurchases"].D());
        Assert.Equal(netPurchases - netCredit, d["netCashPurchases"].D());
        Assert.Equal(1100, d["salesCount"]!.GetValue<int>());
        Assert.Equal(30, d["salesReturnsCount"]!.GetValue<int>());
        Assert.Equal(40, d["purchasesCount"]!.GetValue<int>());
        Assert.Equal(1, d["purchaseReturnsCount"]!.GetValue<int>());
        Assert.Equal(1, d["vouchersCount"]!.GetValue<int>());
        Assert.Equal(1, d["postedJournalEntriesCount"]!.GetValue<int>());

        // الاتجاه الشهري: 9 أشهر بالترتيب، كل شهر بنفس الصيغ
        var monthly = d["monthly"]!.AsArray();
        Assert.Equal(9, monthly.Count);
        for (var i = 0; i < 9; i++)
        {
            var start = MonthStart(8 - i);
            bool InMonth(Invoice x) => x.IssueDate.Year == start.Year && x.IssueDate.Month == start.Month;
            var m = monthly[i]!;
            Assert.Equal(start.Year, m["year"]!.GetValue<int>());
            Assert.Equal(start.Month, m["month"]!.GetValue<int>());
            var mSales = posted.Where(x => x.Kind == InvoiceKind.Sales && InMonth(x)).Sum(x => x.Subtotal) - posted.Where(x => x.Kind == InvoiceKind.SalesReturn && InMonth(x)).Sum(x => x.Subtotal);
            var mCost = posted.Where(x => x.Kind == InvoiceKind.Sales && InMonth(x)).Sum(x => x.TotalCost) - posted.Where(x => x.Kind == InvoiceKind.SalesReturn && InMonth(x)).Sum(x => x.TotalCost);
            Assert.Equal(mSales, m["netSales"].D());
            Assert.Equal(mSales - mCost, m["grossProfit"].D());
            Assert.Equal(posted.Where(x => x.Kind == InvoiceKind.Purchase && InMonth(x)).Sum(x => x.Subtotal)
                - posted.Where(x => x.Kind == InvoiceKind.PurchaseReturn && InMonth(x)).Sum(x => x.Subtotal), m["netPurchases"].D());
        }

        // FinancialStats (SQL) يتفق مع نفس الصيغ
        var stats = (await api.Get("/reports/FinancialStats")).Data!;
        Assert.Equal(netSales, stats["totalSales"].D());
        Assert.Equal(netPurchases, stats["totalPurchases"].D());
        Assert.Equal(netCost, stats["cogsTotal"].D());

        Assert.Equal(400, (await api.Get("/reports/DashboardSummary?months=0")).Status);
    }

    // ---------------- دفتر اليومية العام ----------------

    private static JournalEntry Entry(int n, DateTime date) => new()
    {
        EntryNumber = $"JL-{n:D4}", Date = date, Description = $"قيد-{n}", ReferenceType = "manual", Status = JournalEntryStatus.Posted,
        TotalDebit = n, TotalCredit = n,
        Lines =
        {
            new JournalEntryLine { AccountCode = "1111", AccountName = "الصندوق", Debit = n, Credit = 0 },
            new JournalEntryLine { AccountCode = n == 555 ? "9999" : "4101", AccountName = "إيراد", Debit = 0, Credit = n },
        },
    };

    [Fact]
    public async Task Journal_ledger_pages_filters_and_sums_lines_beyond_1000_in_the_database()
    {
        var api = await NewTenantAsync();
        var day0 = new DateTime(2026, 3, 1, 15, 0, 0);
        var entries = Enumerable.Range(1, 600).Select(n => Entry(n, day0.AddDays(n % 30))).ToList(); // 1200 سطر
        await SeedEntitiesAsync(api.TenantId, entries);

        var summary = Esc("[{\"selector\":\"debit\",\"summaryType\":\"sum\"},{\"selector\":\"credit\",\"summaryType\":\"sum\"}]");
        var page = await api.Get($"/reports/JournalLedger?skip=1000&take=500&requireTotalCount=true&totalSummary={summary}");
        Assert.Equal(200, page.Status);
        Assert.Equal(1200, page.Body!["totalCount"]!.GetValue<int>());
        Assert.Equal(200, page.Body["data"]!.AsArray().Count);
        var expected = entries.Sum(e => e.TotalDebit);
        Assert.Equal(expected, page.Body["summary"]![0].D());
        Assert.Equal(expected, page.Body["summary"]![1].D());

        var byAccount = await api.Get($"/reports/JournalLedger?requireTotalCount=true&filter={Esc("[\"accountCode\",\"=\",\"9999\"]")}");
        Assert.Equal("JL-0555", byAccount.Body!["data"]![0]!["entryNumber"].S());

        var day = day0.AddDays(5).Date;
        var inDay = await api.Get($"/reports/JournalLedger?requireTotalCount=true&dateFrom={day:yyyy-MM-dd}&dateTo={day:yyyy-MM-dd}");
        Assert.Equal(entries.Count(e => e.Date.Date == day) * 2, inDay.Body!["totalCount"]!.GetValue<int>());

        var other = await NewTenantAsync("منشأة ثانية");
        Assert.Equal(0, (await other.Get("/reports/JournalLedger?requireTotalCount=true")).Body!["totalCount"]!.GetValue<int>());
    }

    // ---------------- توزيع مدفوعات المبيعات ----------------

    [Fact]
    public async Task Sales_payment_breakdown_applies_the_payment_rules_and_totals_over_the_whole_dataset()
    {
        var api = await NewTenantAsync();
        var (customerId, _) = await SeedCustomerAsync(api, name: "عميل مسجل");
        var day = new DateTime(2026, 4, 10, 11, 0, 0);

        var bulk = Enumerable.Range(1, 1050).Select(n => Inv($"B-{n:D4}", InvoiceKind.Sales, "posted", day, n, 0, PaymentMethod.Cash)).ToList();
        var credit = Inv("X-CREDIT", InvoiceKind.Sales, "posted", day, 100, 0, PaymentMethod.Credit);
        var draft = Inv("X-DRAFT", InvoiceKind.Sales, "draft", day, 100, 0, PaymentMethod.Cash);
        var card = Inv("X-CARD", InvoiceKind.Sales, "posted", day, 100, 0, PaymentMethod.BankCard);
        var split = Inv("X-SPLIT", InvoiceKind.Sales, "posted", day, 100, 0, PaymentMethod.Cash); // grand 115: 50 نقد + 40 تحويل = جزئي
        split.PaymentSplits.Add(new InvoicePaymentSplit { Method = PaymentMethod.Cash, Amount = 50 });
        split.PaymentSplits.Add(new InvoicePaymentSplit { Method = PaymentMethod.BankTransfer, Amount = 40 });
        var linked = Inv("X-LINKED", InvoiceKind.Sales, "posted", day, 100, 0, PaymentMethod.Credit);
        linked.PartyId = customerId; // بلا هاتف في الفاتورة → من بطاقة العميل
        var vehicle = Inv("X-VEHICLE", InvoiceKind.Sales, "posted", day, 100, 0); // خارج نطاق التقرير
        vehicle.VehicleLines.Add(new InvoiceVehicleLine { LineNo = 1, Vin = "V", BrandNameAr = "ب", ModelNameAr = "م", Year = 2026 });
        var ret = Inv("X-RETURN", InvoiceKind.SalesReturn, "posted", day, 100, 0); // المرتجع ليس فاتورة بيع
        await SeedEntitiesAsync(api.TenantId, bulk.Concat(new[] { credit, draft, card, split, linked, vehicle, ret }));

        async Task<System.Text.Json.Nodes.JsonNode> Row(string number)
        {
            var r = await api.Get($"/reports/SalesPaymentBreakdown?filter={Esc($"[\"number\",\"=\",\"{number}\"]")}");
            Assert.Equal(200, r.Status);
            return r.Body!["data"]!.AsArray().Single()!;
        }
        var c = await Row("X-CREDIT");
        Assert.Equal(0, c["paid"].D()); Assert.Equal(115, c["balance"].D()); Assert.Equal("unpaid", c["paymentStatus"].S());
        var dr = await Row("X-DRAFT");
        Assert.Equal(0, dr["paid"].D()); Assert.Equal("unpaid", dr["paymentStatus"].S());
        var cd = await Row("X-CARD");
        Assert.Equal(115, cd["paidBankCard"].D()); Assert.Equal(0, cd["balance"].D()); Assert.Equal("paid", cd["paymentStatus"].S());
        var sp = await Row("X-SPLIT");
        Assert.Equal(50, sp["paidCash"].D()); Assert.Equal(40, sp["paidBankTransfer"].D()); Assert.Equal(90, sp["paid"].D());
        Assert.Equal(25, sp["balance"].D()); Assert.Equal("partial", sp["paymentStatus"].S());
        Assert.Equal("050", (await Row("X-LINKED"))["phone"].S());

        var all = await api.Get("/reports/SalesPaymentBreakdown?take=1&requireTotalCount=true&totalSummary="
            + Esc("[{\"selector\":\"grand\",\"summaryType\":\"sum\"},{\"selector\":\"paidCash\",\"summaryType\":\"sum\"},{\"selector\":\"balance\",\"summaryType\":\"sum\"}]"));
        Assert.Equal(1050 + 5, all.Body!["totalCount"]!.GetValue<int>()); // بلا فاتورة المركبات والمرتجع
        var rows = bulk.Concat(new[] { credit, draft, card, split, linked }).ToList();
        Assert.Equal(rows.Sum(x => x.GrandTotal), all.Body["summary"]![0].D());
        Assert.Equal(bulk.Sum(x => x.GrandTotal) + 50, all.Body["summary"]![1].D());
        Assert.Equal(115 + 115 + 25 + 115, all.Body["summary"]![2].D()); // آجل + مسودة + جزئي + آجل مرتبط

        var partial = await api.Get($"/reports/SalesPaymentBreakdown?requireTotalCount=true&filter={Esc("[\"paymentStatus\",\"=\",\"partial\"]")}");
        Assert.Equal(1, partial.Body!["totalCount"]!.GetValue<int>());
        var byMethod = await api.Get($"/reports/SalesPaymentBreakdown?requireTotalCount=true&filter={Esc("[\"paidBankTransfer\",\">\",0]")}");
        Assert.Equal("X-SPLIT", byMethod.Body!["data"]![0]!["number"].S());
    }

    // ---------------- البحث العام ----------------

    [Fact]
    public async Task Global_search_covers_permitted_kinds_only_and_finds_records_beyond_1000()
    {
        var owner = await NewTenantAsync();
        var docs = Enumerable.Range(1, 1100).Select(n => Inv($"GS-{n:D4}", InvoiceKind.Sales, "posted", new DateTime(2026, 1, 1).AddMinutes(n), n, 0)).ToList();
        docs.Add(Inv("GS-RET-1", InvoiceKind.SalesReturn, "posted", new DateTime(2026, 5, 1), 1, 0));
        docs.Add(Inv("GS-PUR-1", InvoiceKind.Purchase, "posted", new DateTime(2026, 5, 2), 1, 0));
        docs.Add(Inv("GS-PRET-1", InvoiceKind.PurchaseReturn, "posted", new DateTime(2026, 5, 3), 1, 0)); // خارج نطاق البحث العام
        docs[4].Notes = "ملاحظة-بحث-عام";
        await SeedEntitiesAsync(owner.TenantId, docs);

        var oldest = await owner.Get("/invoices/Search?term=GS-0003");
        Assert.Equal("GS-0003", oldest.Data!["items"]![0]!["invoiceNumber"].S()); // الأقدم: خارج أي "أول 1000" بالترتيب الأحدث
        Assert.Equal("GS-0005", (await owner.Get($"/invoices/Search?term={Esc("ملاحظة-بحث")}")).Data!["items"]![0]!["invoiceNumber"].S());

        var mixed = await owner.Get("/invoices/Search?term=GS-&take=50");
        Assert.Equal(1102, mixed.Data!["totalCount"]!.GetValue<int>()); // بيع + مرتجع بيع + شراء، بلا مرتجع الشراء
        Assert.Equal(50, mixed.Data["items"]!.AsArray().Count);

        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        async Task Revoke(params string[] screens)
        {
            var current = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
            var updated = current.Select(p => new
            {
                screenId = p!["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
                canView = !screens.Contains(p["screenId"].S()) && p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
                canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
            }).ToList();
            Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = updated })).Status);
        }

        await Revoke("purchases");
        Assert.Equal(0, (await rep.Get("/invoices/Search?term=GS-PUR")).Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(1, (await rep.Get("/invoices/Search?term=GS-RET")).Data!["totalCount"]!.GetValue<int>());
        await Revoke("purchases", "sales-returns");
        Assert.Equal(0, (await rep.Get("/invoices/Search?term=GS-RET")).Data!["totalCount"]!.GetValue<int>());
        await Revoke("purchases", "sales-returns", "sales");
        Assert.Equal(403, (await rep.Get("/invoices/Search?term=GS-")).Status);

        var other = await NewTenantAsync("منشأة ثانية");
        Assert.Equal(0, (await other.Get("/invoices/Search?term=GS-")).Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(401, (await new Client(NewHttp()).Get("/invoices/Search?term=GS-")).Status);
    }

    /// <summary>
    /// بلا فرز من العميل يبقى ترتيب التقرير الافتراضي عبر الصفحات. DevExtreme يضيف عند الترقيم ترتيبًا ضمنيًا خاصًا به
    /// (أول عضو، وهو Id عشوائي) لو لم يُمرَّر ترتيب صريح، فتظهر الصفوف بترتيب عشوائي.
    /// </summary>
    [Fact]
    public async Task Paged_reports_keep_their_default_order_without_a_client_sort()
    {
        var api = await NewTenantAsync();
        var start = new DateTime(2026, 6, 1, 9, 0, 0);
        await SeedEntitiesAsync(api.TenantId, Enumerable.Range(1, 60)
            .Select(n => (object)Inv($"ORD-{n:D3}", InvoiceKind.Sales, "posted", start.AddMinutes(n), n, 0))
            .Concat(Enumerable.Range(1, 30).Select(n => Entry(n, start.AddHours(n)))));

        async Task<List<string>> Page(string path, string field, int skip)
            => (await api.Get($"{path}?skip={skip}&take=10")).Body!["data"]!.AsArray().Select(r => r![field].S()).ToList();

        // الأحدث أولًا
        var breakdown = (await Page("/reports/SalesPaymentBreakdown", "number", 0)).Concat(await Page("/reports/SalesPaymentBreakdown", "number", 10)).ToList();
        Assert.Equal(Enumerable.Range(41, 20).Reverse().Select(n => $"ORD-{n:D3}"), breakdown);
        var ledger = (await Page("/reports/JournalLedger", "entryNumber", 0)).Concat(await Page("/reports/JournalLedger", "entryNumber", 10)).ToList();
        Assert.Equal(Enumerable.Range(21, 10).Reverse().SelectMany(n => new[] { $"JL-{n:D4}", $"JL-{n:D4}" }), ledger);

        // كارت الصنف (رصيد جارٍ في الذاكرة): الصفحة الثانية تكمل التسلسل الزمني فيتزايد الرصيد
        var product = await SeedProductAsync(api, stock: 0);
        for (var i = 1; i <= 15; i++)
            Assert.Equal(200, (await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_in", quantity = 1, unitCost = 5, referenceNumber = $"R{i}" })).Status);
        var balances = new List<decimal>();
        foreach (var skip in new[] { 0, 10 })
            balances.AddRange((await api.Get($"/reports/ItemLedger?itemId={product}&skip={skip}&take=10")).Body!["data"]!.AsArray().Select(r => r!["runningStockBalance"].D()));
        Assert.Equal(Enumerable.Range(1, 15).Select(x => (decimal)x), balances);
    }

    [Fact]
    public async Task Report_endpoints_follow_their_screen_permissions()
    {
        var owner = await NewTenantAsync();
        foreach (var path in new[] { "/reports/DashboardSummary", "/reports/JournalLedger", "/reports/SalesPaymentBreakdown" })
            Assert.Equal(401, (await new Client(NewHttp()).Get(path)).Status);

        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.Equal(200, (await rep.Get("/reports/DashboardSummary")).Status);          // لوحة التحكم مسموحة افتراضيًا
        Assert.Equal(403, (await rep.Get("/reports/JournalLedger")).Status);              // التقارير محجوبة عن المندوب
        Assert.Equal(403, (await rep.Get("/reports/SalesPaymentBreakdown")).Status);
    }
}
