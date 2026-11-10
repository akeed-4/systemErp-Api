using ERP.Core.Models.Accounting;
using ERP.Core.Models.CarShowroom;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// قوائم السندات والقيود وعقود بيع السيارات وأوامر شرائها وملخصاتها على بيانات تتجاوز 1000 سجل:
/// الترقيم والبحث والفرز والفترة والإجماليات في قاعدة البيانات، مع عزل المنشآت والصلاحيات.
/// القيم المتوقعة تُحسب في C# من البيانات المزروعة.
/// </summary>
[Collection("api")]
public class DocumentListTests : TestBase
{
    public DocumentListTests(ErpFactory f) : base(f) { }

    private const int Bulk = 1050;
    private static readonly DateTime Day0 = new(2026, 2, 1, 16, 45, 0); // وقت غير منتصف الليل لاختبار شمول اليوم الأخير
    private static DateTime DateOf(int n) => Day0.AddDays((n - 1) % 50); // 21 مستندًا لكل يوم
    private static string Esc(string s) => Uri.EscapeDataString(s);
    private static int Total(Res r) => r.Data!["totalCount"]!.GetValue<int>();
    private static List<string> Field(Res r, string name) => r.Data!["items"]!.AsArray().Select(i => i![name].S()).ToList();

    /// <summary>
    /// السيناريو المشترك لأي قائمة: لا قصّ عند 1000، صفحات بلا تداخل، سجل خارج أول 1000 يُعثر عليه بالبحث،
    /// فرز في قاعدة البيانات وحقل فرز غير معروف = 400، واليوم الأخير في الفترة شامل.
    /// </summary>
    private static async Task AssertPagingSearchSortAsync(Client api, string path, string numberField, string sortField,
        string prefix, int perDay, string extra = "")
    {
        var first = await api.Get($"{path}?pageSize=1000&sortBy={numberField}&isDescending=false{extra}");
        Assert.Equal(200, first.Status);
        Assert.Equal(Bulk, Total(first));
        var firstNumbers = Field(first, numberField);
        Assert.Equal(1000, firstNumbers.Count);
        var target = $"{prefix}{1040:D4}";
        Assert.DoesNotContain(target, firstNumbers);

        var second = Field(await api.Get($"{path}?pageNumber=2&pageSize=1000&sortBy={numberField}&isDescending=false{extra}"), numberField);
        Assert.Equal(Bulk - 1000, second.Count);
        Assert.Equal(Bulk, firstNumbers.Concat(second).Distinct().Count());
        Assert.Equal($"{prefix}{1001:D4}", second.First());

        var found = await api.Get($"{path}?pageSize=5&searchTerm={target}{extra}");
        Assert.Equal(1, Total(found));
        Assert.Equal(target, Field(found, numberField).Single());

        var top = Field(await api.Get($"{path}?pageSize=1&sortBy={sortField}&isDescending=true{extra}"), numberField);
        Assert.Equal($"{prefix}{Bulk:D4}", top.Single()); // المبلغ يتزايد مع الرقم
        Assert.Equal(400, (await api.Get($"{path}?sortBy=noSuchColumn{extra}")).Status);

        var day = DateOf(5).Date;
        var inDay = await api.Get($"{path}?startDate={day:yyyy-MM-dd}&endDate={day:yyyy-MM-dd}{extra}");
        Assert.Equal(perDay, Total(inDay));
    }

    // ---------------- السندات ----------------

    private static Voucher NewVoucher(int n, string prefix = "VCH-") => new()
    {
        VoucherNumber = $"{prefix}{n:D4}", Type = n % 4 == 0 ? VoucherType.Payment : VoucherType.Receipt, Date = DateOf(n),
        Amount = n * 2m, AmountInWordsAr = "-", PartyName = $"طرف-{n}", PartyAccountCode = "112", TreasuryAccountCode = "1111",
        PaymentMethod = "cash", Notes = n == 777 ? "ملاحظة-سند-فريدة" : string.Empty,
    };

    [Fact]
    public async Task Vouchers_page_search_sort_and_summarize_over_more_than_1000_records()
    {
        var api = await NewTenantAsync();
        var seeded = Enumerable.Range(1, Bulk).Select(n => NewVoucher(n)).ToList();
        await SeedEntitiesAsync(api.TenantId, seeded);

        await AssertPagingSearchSortAsync(api, "/vouchers", "voucherNumber", "amount", "VCH-", 21);
        Assert.Equal("VCH-0777", Field(await api.Get($"/vouchers?searchTerm={Esc("ملاحظة-سند-فريدة")}"), "voucherNumber").Single());

        var receipts = seeded.Where(v => v.Type == VoucherType.Receipt).ToList();
        var payments = seeded.Where(v => v.Type == VoucherType.Payment).ToList();
        var s = (await api.Get("/vouchers/Summary?pageSize=10")).Data!;
        Assert.Equal(Bulk, s["count"]!.GetValue<int>());
        Assert.Equal(receipts.Count, s["receiptCount"]!.GetValue<int>());
        Assert.Equal(receipts.Sum(v => v.Amount), s["receiptTotal"].D());
        Assert.Equal(payments.Count, s["paymentCount"]!.GetValue<int>());
        Assert.Equal(payments.Sum(v => v.Amount), s["paymentTotal"].D());
        Assert.Equal(receipts.Sum(v => v.Amount) - payments.Sum(v => v.Amount), s["netCashFlow"].D());

        // نوع السند (status) يطبَّق على القائمة والملخص معًا
        var onlyPayments = await api.Get("/vouchers/Summary?status=payment");
        Assert.Equal(payments.Count, onlyPayments.Data!["count"]!.GetValue<int>());
        Assert.Equal(0, onlyPayments.Data["receiptTotal"].D());
        Assert.Equal(payments.Count, Total(await api.Get("/vouchers?status=payment&pageSize=1")));
    }

    // ---------------- القيود ----------------

    private static JournalEntry NewEntry(int n, string prefix = "JE-") => new()
    {
        EntryNumber = $"{prefix}{n:D4}", Date = DateOf(n), Description = $"قيد-{n}",
        ReferenceType = n % 5 == 0 ? "invoice" : "manual", Status = JournalEntryStatus.Posted,
        TotalDebit = n * 3m, TotalCredit = n == 13 ? n * 3m - 1 : n * 3m, // قيد واحد غير متوازن عمدًا
    };

    [Fact]
    public async Task Journal_entries_page_search_sort_filter_by_reference_type_and_summarize()
    {
        var api = await NewTenantAsync();
        var seeded = Enumerable.Range(1, Bulk).Select(n => NewEntry(n)).ToList();
        await SeedEntitiesAsync(api.TenantId, seeded);

        await AssertPagingSearchSortAsync(api, "/journalentries", "entryNumber", "totalDebit", "JE-", 21);

        // قيد بلا نوع مرجع يُعدّ يدويًا
        var legacy = NewEntry(9999); legacy.ReferenceType = null;
        await SeedEntitiesAsync(api.TenantId, new[] { legacy });
        seeded.Add(legacy);
        var manual = seeded.Where(e => e.ReferenceType is null or "manual").ToList();
        Assert.Equal(manual.Count, Total(await api.Get("/journalentries?referenceType=manual&pageSize=1")));
        Assert.Equal("JE-9999", Field(await api.Get("/journalentries?referenceType=manual&searchTerm=JE-9999"), "entryNumber").Single());
        Assert.Equal(seeded.Count(e => e.ReferenceType == "invoice"), Total(await api.Get("/journalentries?referenceType=invoice&pageSize=1")));
        Assert.All(Field(await api.Get("/journalentries?referenceType=manual&pageSize=1000"), "referenceType"), t => Assert.True(t is "manual" or "", t));

        var s = (await api.Get("/journalentries/Summary?referenceType=manual")).Data!;
        Assert.Equal(manual.Count, s["count"]!.GetValue<int>());
        Assert.Equal(manual.Sum(e => e.TotalDebit), s["totalDebit"].D());
        Assert.Equal(manual.Sum(e => e.TotalCredit), s["totalCredit"].D());
        Assert.Equal(1, s["unbalancedCount"]!.GetValue<int>());

        var all = (await api.Get("/journalentries/Summary")).Data!;
        Assert.Equal(Bulk + 1, all["count"]!.GetValue<int>());
        Assert.Equal(seeded.Sum(e => e.TotalDebit), all["totalDebit"].D());
    }

    // ---------------- عقود بيع السيارات ----------------

    private static async Task<Guid> SeedVehicleAsync(Client api)
    {
        var car = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('L'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = "new",
            purchasePrice = 40000, sellingPrice = 50000, vatMode = "standard_15", fuelType = "petrol", transmission = "automatic",
            colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض" });
        Assert.Equal(201, car.Status);
        return car.Data!["id"].G();
    }

    private static CarSalesContract NewContract(int n, Guid vehicleId, string prefix = "CSC-") => new()
    {
        ContractNumber = $"{prefix}{n:D4}", Date = DateOf(n), CycleType = n % 2 == 0 ? CarSalesCycleType.Corporate : CarSalesCycleType.Individual,
        Status = SalesContractStatus.Invoiced, BuyerName = $"مشتري-{n}", BuyerNationalIdOrCr = "1010101010", BuyerPhone = "0550000000",
        VehicleId = vehicleId, Vin = $"VIN{n:D6}", VehicleDescription = n == 888 ? "لاندكروزر-فريدة" : "يارس",
        CostPrice = n * 4m, SellingPrice = n * 5m, NetPriceBeforeVat = n % 3 == 0 ? null : n * 5m - 1,
        ProfitMargin = n * 1m, VatAmount = n * 0.75m, TotalWithVat = n * 5.75m,
    };

    [Fact]
    public async Task Car_sales_contracts_page_search_sort_and_summarize_over_more_than_1000_records()
    {
        var api = await NewTenantAsync();
        var vehicle = await SeedVehicleAsync(api);
        var seeded = Enumerable.Range(1, Bulk).Select(n => NewContract(n, vehicle)).ToList();
        await SeedEntitiesAsync(api.TenantId, seeded);

        await AssertPagingSearchSortAsync(api, "/carsalescontracts", "contractNumber", "totalWithVat", "CSC-", 21);
        // البحث يشمل وصف المركبة المعروض في الجدول
        Assert.Equal("CSC-0888", Field(await api.Get($"/carsalescontracts?searchTerm={Esc("لاندكروزر-فريدة")}"), "contractNumber").Single());

        void AssertSummary(Res r, List<CarSalesContract> expected)
        {
            var d = r.Data!;
            Assert.Equal(expected.Count, d["count"]!.GetValue<int>());
            Assert.Equal(expected.Sum(c => c.NetPriceBeforeVat ?? c.SellingPrice), d["netBeforeVat"].D());
            Assert.Equal(expected.Sum(c => c.VatAmount), d["vatAmount"].D());
            Assert.Equal(expected.Sum(c => c.TotalWithVat), d["totalWithVat"].D());
            Assert.Equal(expected.Sum(c => c.CostPrice), d["costPrice"].D());
            Assert.Equal(expected.Sum(c => c.ProfitMargin), d["profitMargin"].D());
        }
        AssertSummary(await api.Get("/carsalescontracts/Summary"), seeded);
        AssertSummary(await api.Get("/carsalescontracts/Summary?cycleType=corporate"), seeded.Where(c => c.CycleType == CarSalesCycleType.Corporate).ToList());
        Assert.Equal(400, (await api.Get("/carsalescontracts/Summary?cycleType=bogus")).Status);
    }

    // ---------------- أوامر شراء السيارات ----------------

    private static CarProcurementOrder NewOrder(int n, Guid supplierId, string prefix = "CPO-") => new()
    {
        OrderNumber = $"{prefix}{n:D4}", Date = DateOf(n), SupplierId = supplierId, SupplierName = $"وكيل-{n}",
        PurchaseCycle = "individual", Stage = ProcurementStage.Requisition, Status = ProcurementOrderStatus.Draft,
        PaymentType = (n % 4) switch { 0 => "cash", 1 => "credit", 2 => "bank_lc", _ => "advance_milestone" },
        Subtotal = n * 10m, VatTotal = n * 1.5m, GrandTotal = n * 11.5m,
    };

    [Fact]
    public async Task Car_procurement_orders_page_search_sort_and_summarize_over_more_than_1000_records()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var seeded = Enumerable.Range(1, Bulk).Select(n => NewOrder(n, supplier)).ToList();
        var withVin = seeded[29]; // CPO-0030
        withVin.ReceivedVins.Add(new CarProcurementOrderVin { Vin = "JTDVIN-UNIQUE-30" });
        await SeedEntitiesAsync(api.TenantId, seeded);

        await AssertPagingSearchSortAsync(api, "/carprocurementorders", "orderNumber", "grandTotal", "CPO-", 21, "&purchaseCycle=individual");
        Assert.Equal("CPO-0030", Field(await api.Get("/carprocurementorders?searchTerm=JTDVIN-UNIQUE"), "orderNumber").Single());

        // غير النقدي (credit / bank_lc / advance_milestone) آجل، كقاعدة فوترة الأمر
        var cash = seeded.Where(o => o.PaymentType == "cash").ToList();
        var credit = seeded.Where(o => o.PaymentType != "cash").ToList();
        var d = (await api.Get("/carprocurementorders/Summary?purchaseCycle=individual")).Data!;
        Assert.Equal(Bulk, d["count"]!.GetValue<int>());
        Assert.Equal(seeded.Sum(o => o.Subtotal), d["subtotal"].D());
        Assert.Equal(seeded.Sum(o => o.VatTotal), d["vatTotal"].D());
        Assert.Equal(seeded.Sum(o => o.GrandTotal), d["grandTotal"].D());
        Assert.Equal(cash.Count, d["cashCount"]!.GetValue<int>());
        Assert.Equal(cash.Sum(o => o.GrandTotal), d["cashGrandTotal"].D());
        Assert.Equal(credit.Count, d["creditCount"]!.GetValue<int>());
        Assert.Equal(credit.Sum(o => o.GrandTotal), d["creditGrandTotal"].D());
        Assert.Equal(0, (await api.Get("/carprocurementorders/Summary?purchaseCycle=corporate")).Data!["count"]!.GetValue<int>());
    }

    // ---------------- العزل والصلاحيات ----------------

    [Fact]
    public async Task Lists_and_summaries_are_isolated_per_tenant()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync("منشأة ثانية");
        await SeedEntitiesAsync(a.TenantId, Enumerable.Range(1, 30).Select(n => (object)NewVoucher(n)).Concat(Enumerable.Range(1, 30).Select(n => NewEntry(n))));
        // نفس الأرقام في المنشأة الثانية بعدد أقل
        await SeedEntitiesAsync(b.TenantId, Enumerable.Range(1, 3).Select(n => (object)NewVoucher(n)).Concat(Enumerable.Range(1, 2).Select(n => NewEntry(n))));

        Assert.Equal(3, Total(await b.Get("/vouchers?searchTerm=VCH-")));
        Assert.Equal(3, (await b.Get("/vouchers/Summary")).Data!["count"]!.GetValue<int>());
        Assert.Equal(new[] { 1, 2, 3 }.Sum(n => n * 2m), (await b.Get("/vouchers/Summary")).Data!["receiptTotal"].D());
        Assert.Equal(2, (await b.Get("/journalentries/Summary")).Data!["count"]!.GetValue<int>());

        var aVoucher = Field(await a.Get("/vouchers?searchTerm=VCH-0007"), "id").Single();
        Assert.Equal(404, (await b.Get($"/vouchers/{aVoucher}")).Status);
        var aEntry = Field(await a.Get("/journalentries?searchTerm=JE-0007"), "id").Single();
        Assert.Equal(404, (await b.Get($"/journalentries/{aEntry}")).Status);
        Assert.Equal(0, (await b.Get("/carsalescontracts/Summary")).Data!["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task Summaries_require_the_same_screen_permission_as_their_lists()
    {
        var owner = await NewTenantAsync();
        foreach (var path in new[] { "/vouchers/Summary", "/journalentries/Summary", "/carsalescontracts/Summary", "/carprocurementorders/Summary" })
            Assert.Equal(401, (await new Client(NewHttp()).Get(path)).Status);

        var email = $"s{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // المندوب افتراضيًا محجوب عن شاشة الحسابات (القيود)
        Assert.Equal(403, (await rep.Get("/journalentries")).Status);
        Assert.Equal(403, (await rep.Get("/journalentries/Summary")).Status);

        // سحب عرض السندات ومعرض السيارات
        var current = (await owner.Get("/permissions?roleId=sales_rep")).Data!.AsArray();
        var updated = current.Select(p => new
        {
            screenId = p!["screenId"].S(), screenNameAr = p["screenNameAr"].S(), screenNameEn = p["screenNameEn"].S(),
            canView = p["screenId"].S() is not ("vouchers" or "car-showroom") && p["canView"]!.GetValue<bool>(), canCreate = p["canCreate"]!.GetValue<bool>(),
            canEdit = p["canEdit"]!.GetValue<bool>(), canDelete = p["canDelete"]!.GetValue<bool>(), canApprove = p["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = "sales_rep", permissions = updated })).Status);

        foreach (var path in new[] { "/vouchers", "/vouchers/Summary", "/carsalescontracts", "/carsalescontracts/Summary", "/carprocurementorders", "/carprocurementorders/Summary" })
            Assert.True((await rep.Get(path)).Status == 403, path);
        Assert.Equal(200, (await owner.Get("/vouchers/Summary")).Status);
    }
}
