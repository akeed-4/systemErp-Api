using ERP.Core.Models.Accounting;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// قائمة الفواتير وملخصها على بيانات تتجاوز 1000 سجل: الترقيم والبحث والفرز والفترة والإجماليات كلها في قاعدة البيانات،
/// مع عزل المنشآت والصلاحيات. القيم المتوقعة تُحسب في C# من نفس البيانات المزروعة لا من الخادم.
/// </summary>
[Collection("api")]
public class InvoiceListTests : TestBase
{
    public InvoiceListTests(ErpFactory f) : base(f) { }

    private const int Bulk = 1100;
    private static readonly DateTime Day0 = new(2026, 1, 1, 14, 30, 0); // وقت غير منتصف الليل لاختبار شمول اليوم الأخير

    private sealed record Seeded(int N, string Number, string Party, DateTime Date, PaymentMethod Method,
        decimal Subtotal, decimal Vat, decimal Grand, decimal Cost, decimal Profit, string? Notes);

    /// <summary>1100 فاتورة بيع مرحّلة: كل ثالثة آجلة، 11 فاتورة لكل يوم على 100 يوم، ورقم 1040 له ملاحظة مميزة.</summary>
    private static List<Seeded> BulkSales() => Enumerable.Range(1, Bulk).Select(n => new Seeded(
        n, $"BULK-{n:D4}", $"عميل-{n}", Day0.AddDays((n - 1) % 100),
        n % 3 == 0 ? PaymentMethod.Credit : (n % 2 == 0 ? PaymentMethod.BankCard : PaymentMethod.Cash),
        n, n * 0.15m, n * 1.15m, n * 0.6m, n * 0.4m, n == 1040 ? "ملاحظة-فريدة-1040" : null)).ToList();

    private static Invoice ToInvoice(Seeded s, InvoiceKind kind = InvoiceKind.Sales) => new()
    {
        InvoiceNumber = s.Number, IssueDate = s.Date, Kind = kind, InvoiceType = InvoiceType.Simplified,
        PartyName = s.Party, PaymentMethod = s.Method, Status = "posted",
        Subtotal = s.Subtotal, DiscountTotal = 0, VatTotal = s.Vat, GrandTotal = s.Grand,
        TotalCost = s.Cost, GrossProfit = s.Profit, Notes = s.Notes,
    };

    private Task SeedAsync(Guid tenantId, IEnumerable<Invoice> invoices) => SeedEntitiesAsync(tenantId, invoices);

    private async Task<(Client Api, List<Seeded> Sales)> BulkTenantAsync()
    {
        var api = await NewTenantAsync();
        var sales = BulkSales();
        var others = Enumerable.Range(1, 20).Select(n => ToInvoice(new Seeded(n, $"SRET-X{n}", "مرتجع", Day0, PaymentMethod.Cash, 7, 1, 8, 0, 0, null), InvoiceKind.SalesReturn))
            .Concat(Enumerable.Range(1, 30).Select(n => ToInvoice(new Seeded(n, $"PUR-X{n}", "مورد", Day0, PaymentMethod.Credit, 9, 1, 10, 0, 0, null), InvoiceKind.Purchase)));
        await SeedAsync(api.TenantId, sales.Select(s => ToInvoice(s)).Concat(others));
        return (api, sales);
    }

    private static string Esc(string s) => Uri.EscapeDataString(s);
    private static List<string> Numbers(Res r) => r.Data!["items"]!.AsArray().Select(i => i!["invoiceNumber"].S()).ToList();

    [Fact]
    public async Task Paging_never_truncates_at_1000_and_pages_do_not_overlap()
    {
        var (api, sales) = await BulkTenantAsync();

        var first = await api.Get("/invoices?kind=sales&pageNumber=1&pageSize=1000");
        Assert.Equal(200, first.Status);
        Assert.Equal(Bulk, first.Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(1000, Numbers(first).Count);
        Assert.True(first.Data["hasNextPage"]!.GetValue<bool>());

        var second = await api.Get("/invoices?kind=sales&pageNumber=2&pageSize=1000");
        Assert.Equal(Bulk - 1000, Numbers(second).Count);
        var all = Numbers(first).Concat(Numbers(second)).ToList();
        Assert.Equal(Bulk, all.Distinct().Count());
        Assert.True(sales.Select(s => s.Number).ToHashSet().SetEquals(all));

        // حجم أكبر من الحد يُقصّ إلى 1000 علنًا: pageSize و totalCount يكشفان أن هناك صفحة أخرى
        var oversized = await api.Get("/invoices?kind=sales&pageSize=5000");
        Assert.Equal(1000, oversized.Data!["pageSize"]!.GetValue<int>());
        Assert.Equal(Bulk, oversized.Data["totalCount"]!.GetValue<int>());
        Assert.Equal(2, oversized.Data["totalPages"]!.GetValue<int>());
    }

    [Fact]
    public async Task Search_finds_records_beyond_the_first_1000()
    {
        var (api, _) = await BulkTenantAsync();

        // بالترتيب التصاعدي للرقم تضم الصفحة الأولى (1000 سجل) BULK-0001..BULK-1000 فقط: BULK-1040 خارجها فعلًا
        var firstPage = Numbers(await api.Get("/invoices?kind=sales&pageSize=1000&sortBy=invoiceNumber&isDescending=false"));
        Assert.Equal(1000, firstPage.Count);
        Assert.DoesNotContain("BULK-1040", firstPage);

        var byNumber = await api.Get("/invoices?kind=sales&pageSize=10&searchTerm=BULK-1040");
        Assert.Equal(1, byNumber.Data!["totalCount"]!.GetValue<int>());
        Assert.Equal("BULK-1040", Numbers(byNumber).Single());

        var byNotes = await api.Get($"/invoices?kind=sales&searchTerm={Esc("ملاحظة-فريدة")}");
        Assert.Equal("BULK-1040", Numbers(byNotes).Single());

        var exact = await api.Get("/invoices?kind=sales&invoiceNumber=BULK-1099");
        Assert.Equal("BULK-1099", Numbers(exact).Single());

        var id = byNumber.Data["items"]![0]!["id"].S();
        var byId = await api.Get($"/invoices?kind=sales&searchTerm={id}");
        Assert.Equal("BULK-1040", Numbers(byId).Single());
        Assert.Equal(200, (await api.Get($"/invoices/{id}")).Status);

        // البحث ضمن النوع فقط: رقم مرتجع لا يظهر في قائمة المبيعات
        Assert.Equal(0, (await api.Get("/invoices?kind=sales&searchTerm=SRET-X1")).Data!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Sorting_is_applied_in_the_database_across_pages_and_unknown_columns_are_rejected()
    {
        var (api, _) = await BulkTenantAsync();

        var asc = Numbers(await api.Get("/invoices?kind=sales&pageSize=3&sortBy=grandTotal&isDescending=false"));
        Assert.Equal(new[] { "BULK-0001", "BULK-0002", "BULK-0003" }, asc);
        var desc = Numbers(await api.Get("/invoices?kind=sales&pageSize=3&sortBy=GrandTotal&isDescending=true"));
        Assert.Equal(new[] { "BULK-1100", "BULK-1099", "BULK-1098" }, desc);

        // الصفحة 2 بحجم 1000 تبدأ من السجل 1001 في الترتيب التصاعدي
        var page2 = Numbers(await api.Get("/invoices?kind=sales&pageNumber=2&pageSize=1000&sortBy=invoiceNumber&isDescending=false"));
        Assert.Equal("BULK-1001", page2.First());
        Assert.Equal("BULK-1100", page2.Last());

        Assert.Equal(400, (await api.Get("/invoices?kind=sales&sortBy=noSuchColumn")).Status);
        Assert.Equal(400, (await api.Get("/invoices?kind=sales&paymentType=bogus")).Status);
    }

    [Fact]
    public async Task Summary_totals_cover_the_complete_filtered_dataset_not_the_page()
    {
        var (api, sales) = await BulkTenantAsync();

        void AssertSummary(Res r, IReadOnlyCollection<Seeded> expected)
        {
            Assert.Equal(200, r.Status);
            var d = r.Data!;
            Assert.Equal(expected.Count, d["count"]!.GetValue<int>());
            Assert.Equal(expected.Sum(s => s.Subtotal), d["subtotal"].D());
            Assert.Equal(expected.Sum(s => s.Vat), d["vatTotal"].D());
            Assert.Equal(expected.Sum(s => s.Grand), d["grandTotal"].D());
            Assert.Equal(expected.Sum(s => s.Cost), d["totalCost"].D());
            Assert.Equal(expected.Sum(s => s.Profit), d["grossProfit"].D());
            var credit = expected.Where(s => s.Method == PaymentMethod.Credit).ToList();
            Assert.Equal(credit.Count, d["creditCount"]!.GetValue<int>());
            Assert.Equal(credit.Sum(s => s.Grand), d["creditGrandTotal"].D());
            Assert.Equal(expected.Count - credit.Count, d["cashCount"]!.GetValue<int>());
            Assert.Equal(expected.Sum(s => s.Grand) - credit.Sum(s => s.Grand), d["cashGrandTotal"].D());
        }

        // pageSize لا يؤثر على الملخص
        AssertSummary(await api.Get("/invoices/Summary?kind=sales&pageSize=10"), sales);
        AssertSummary(await api.Get("/invoices/Summary?kind=sales&paymentType=credit"), sales.Where(s => s.Method == PaymentMethod.Credit).ToList());
        AssertSummary(await api.Get("/invoices/Summary?kind=sales&paymentType=cash"), sales.Where(s => s.Method != PaymentMethod.Credit).ToList());
        AssertSummary(await api.Get($"/invoices/Summary?kind=sales&searchTerm={Esc("عميل-104")}"), sales.Where(s => s.Party.Contains("عميل-104")).ToList());

        // الملخص والقائمة يطابقان نفس المرشّحات
        var list = await api.Get($"/invoices?kind=sales&pageSize=1&searchTerm={Esc("عميل-104")}&paymentType=credit");
        var summary = await api.Get($"/invoices/Summary?kind=sales&searchTerm={Esc("عميل-104")}&paymentType=credit");
        Assert.Equal(list.Data!["totalCount"]!.GetValue<int>(), summary.Data!["count"]!.GetValue<int>());

        // نوع آخر لا يختلط بالمبيعات
        var purchases = await api.Get("/invoices/Summary?kind=purchase");
        Assert.Equal(30, purchases.Data!["count"]!.GetValue<int>());
        Assert.Equal(300, purchases.Data["grandTotal"].D());

        // نتيجة فارغة = أصفار لا خطأ
        var none = await api.Get("/invoices/Summary?kind=sales&searchTerm=NO-SUCH");
        Assert.Equal(0, none.Data!["count"]!.GetValue<int>());
        Assert.Equal(0, none.Data["grandTotal"].D());
    }

    [Fact]
    public async Task End_date_includes_the_whole_last_day()
    {
        var (api, sales) = await BulkTenantAsync();
        var day = Day0.AddDays(4).Date; // الفواتير في هذا اليوم مؤرخة 14:30
        var expected = sales.Count(s => s.Date.Date == day);
        Assert.Equal(11, expected);

        var r = await api.Get($"/invoices?kind=sales&startDate={day:yyyy-MM-dd}&endDate={day:yyyy-MM-dd}");
        Assert.Equal(expected, r.Data!["totalCount"]!.GetValue<int>());
        var s = await api.Get($"/invoices/Summary?kind=sales&startDate={day:yyyy-MM-dd}&endDate={day:yyyy-MM-dd}");
        Assert.Equal(sales.Where(x => x.Date.Date == day).Sum(x => x.Grand), s.Data!["grandTotal"].D());

        // وقت صريح يبقى حدًّا دقيقًا: قبل 14:30 لا يشمل فواتير ذلك اليوم
        var before = await api.Get($"/invoices?kind=sales&startDate={day:yyyy-MM-dd}&endDate={Esc(day.AddHours(14).ToString("yyyy-MM-ddTHH:mm:ss"))}");
        Assert.Equal(0, before.Data!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Tenants_never_see_each_others_invoices_or_totals()
    {
        var (a, _) = await BulkTenantAsync();
        var b = await NewTenantAsync("منشأة ثانية");
        // نفس رقم الفاتورة في المنشأة الثانية بقيمة مختلفة
        await SeedAsync(b.TenantId, new[] { ToInvoice(new Seeded(1, "BULK-1040", "عميل-ب", Day0, PaymentMethod.Cash, 50, 5, 55, 0, 0, null)) });

        var mine = await b.Get("/invoices?kind=sales&searchTerm=BULK");
        Assert.Equal(1, mine.Data!["totalCount"]!.GetValue<int>());
        Assert.Equal(55, mine.Data["items"]![0]!["grandTotal"].D());
        var summary = await b.Get("/invoices/Summary?kind=sales");
        Assert.Equal(1, summary.Data!["count"]!.GetValue<int>());
        Assert.Equal(55, summary.Data["grandTotal"].D());

        var aInvoiceId = (await a.Get("/invoices?kind=sales&invoiceNumber=BULK-1040")).Data!["items"]![0]!["id"].S();
        Assert.Equal(404, (await b.Get($"/invoices/{aInvoiceId}")).Status);
        Assert.Equal(0, (await b.Get($"/invoices?kind=sales&searchTerm={aInvoiceId}")).Data!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task List_and_summary_require_the_screen_permission_of_the_document_kind()
    {
        var owner = await NewTenantAsync();
        Assert.Equal(401, (await new Client(NewHttp()).Get("/invoices/Summary?kind=sales")).Status);

        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // سحب عرض شاشة المشتريات من دور المندوب
        var current = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
        var updated = current.Select(p => new
        {
            screenId = p!["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
            canView = p["screenId"].S() != "purchases" && p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = updated })).Status);

        Assert.Equal(200, (await rep.Get("/invoices?kind=sales")).Status);
        Assert.Equal(200, (await rep.Get("/invoices/Summary?kind=sales")).Status);
        Assert.Equal(403, (await rep.Get("/invoices?kind=purchase")).Status);
        Assert.Equal(403, (await rep.Get("/invoices/Summary?kind=purchase")).Status);
        Assert.Equal(403, (await rep.Get("/invoices/Summary")).Status); // بلا نوع يلزم المبيعات والمشتريات معًا
    }

    [Fact]
    public async Task Returns_can_be_filtered_by_whether_the_original_invoice_has_vehicle_lines()
    {
        var api = await NewTenantAsync();
        var car = ToInvoice(new Seeded(1, "PUR-CAR", "وكيل", Day0, PaymentMethod.Credit, 100, 15, 115, 0, 0, null), InvoiceKind.Purchase);
        car.VehicleLines.Add(new InvoiceVehicleLine { LineNo = 1, Vin = "VIN-1", BrandNameAr = "تويوتا", ModelNameAr = "كامري", Year = 2026 });
        var plain = ToInvoice(new Seeded(2, "PUR-PLAIN", "مورد", Day0, PaymentMethod.Cash, 100, 15, 115, 0, 0, null), InvoiceKind.Purchase);
        await SeedAsync(api.TenantId, new[] { car, plain });

        var carReturn = ToInvoice(new Seeded(3, "PRET-CAR", "وكيل", Day0, PaymentMethod.Credit, 10, 1.5m, 11.5m, 0, 0, null), InvoiceKind.PurchaseReturn);
        carReturn.OriginalInvoiceId = car.Id; carReturn.OriginalInvoiceNumber = car.InvoiceNumber; carReturn.IsReturn = true; carReturn.ReturnReason = "عيب مصنعي";
        var plainReturn = ToInvoice(new Seeded(4, "PRET-PLAIN", "مورد", Day0, PaymentMethod.Cash, 20, 3, 23, 0, 0, null), InvoiceKind.PurchaseReturn);
        plainReturn.OriginalInvoiceId = plain.Id; plainReturn.OriginalInvoiceNumber = plain.InvoiceNumber; plainReturn.IsReturn = true;
        await SeedAsync(api.TenantId, new[] { carReturn, plainReturn });

        Assert.Equal(new[] { "PRET-CAR" }, Numbers(await api.Get("/invoices?kind=purchase_return&originalHasVehicleLines=true")));
        Assert.Equal(new[] { "PRET-PLAIN" }, Numbers(await api.Get("/invoices?kind=purchase_return&originalHasVehicleLines=false")));
        Assert.Equal(11.5m, (await api.Get("/invoices/Summary?kind=purchase_return&originalHasVehicleLines=true")).Data!["grandTotal"].D());

        // البحث بالرقم الأصلي وسبب المرتجع ورقم شاسيه الفاتورة الأصلية
        Assert.Equal(new[] { "PRET-CAR" }, Numbers(await api.Get("/invoices?kind=purchase_return&searchTerm=PUR-CAR")));
        Assert.Equal(new[] { "PRET-CAR" }, Numbers(await api.Get("/invoices?kind=purchase_return&searchTerm=VIN-1")));
        Assert.Equal(new[] { "PUR-CAR" }, Numbers(await api.Get("/invoices?kind=purchase&searchTerm=VIN-1")));
        Assert.Equal(new[] { "PRET-CAR" }, Numbers(await api.Get($"/invoices?kind=purchase_return&searchTerm={Esc("عيب مصنعي")}")));
    }
}
