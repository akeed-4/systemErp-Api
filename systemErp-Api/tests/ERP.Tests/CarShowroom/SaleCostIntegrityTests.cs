using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تكلفة السيارة وقت البيع لا تتغير ولا تُحسب مرتين: مصروف ما بعد البيع على تكلفة المبيعات مرة واحدة، وحساب المخزون
/// يساوي دائماً تكلفة السيارة المتاحة، وتكلفة المبيعات = تكلفتها وقت البيع + مصاريف ما بعد البيع.
/// </summary>
[Collection("api")]
public class SaleCostIntegrityTests : TestBase
{
    public SaleCostIntegrityTests(ErpFactory f) : base(f) { }

    private const string Inventory = "1142", Cogs = "512";

    private static async Task<decimal> BalanceAsync(Client api, string code)
    {
        var r = await api.Get($"/accounts/ByCode/{code}");
        return r.Status == 200 ? r.Data!["balance"].D() : 0;
    }

    private static async Task AssertBalancedAsync(Client api)
    {
        decimal d = 0, c = 0;
        foreach (var row in (await api.Get("/reports/TrialBalance")).Data!.AsArray()) { d += row!["periodDebit"].D(); c += row["periodCredit"].D(); }
        Assert.Equal(d, c);
    }

    /// <summary>سيارة بتكلفة 40000 وأرصدة الحسابين قبل بيعها: StockBase = رصيد المخزون بدونها.</summary>
    private sealed record Car(string Id, decimal StockBase, decimal CogsBase);

    private static async Task<Car> SeedCarAsync(Client api, string vatMode = "standard_15")
    {
        var car = await api.Post("/vehicles", new
        {
            // هامش الربح للسيارة المستعملة وحدها
            chassisNumber = Client.NewVin('5'), brandNameAr = "تويوتا", modelNameAr = "يارس", year = 2024, condition = vatMode == "standard_15" ? "new" : "used", purchasePrice = 40000, sellingPrice = 50000,
            vatMode, fuelType = "petrol", transmission = "automatic", colorExterior = "أبيض", colorInterior = "أسود", location = "المعرض",
        });
        Assert.Equal(201, car.Status);
        var id = car.Data!["id"].S();
        var cost = (await api.Get($"/vehicles/{id}")).Data!["totalCost"].D();
        return new Car(id, await BalanceAsync(api, Inventory) - cost, await BalanceAsync(api, Cogs));
    }

    private static object Sale(string status, string vehicleId, decimal price) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status,
        revenueAccountCode = "412", cogsAccountCode = Cogs, inventoryAccountCode = Inventory,
        vehicleLines = new[] { new { vehicleId, unitPrice = price, discount = 0, vatMode = "standard_15" } },
    };

    private static async Task<Res> LateChargeAsync(Client api, string vehicleId, decimal amount)
    {
        var types = (await api.Get("/expensetypes?pageSize=200")).Data!["items"]!.AsArray().ToDictionary(t => t!["code"].S(), t => t!["id"].S());
        return await api.Post("/additionalcharges", new
        { documentType = "vehicle", documentId = vehicleId, expenseTypeId = types["REPAIR"], amount, status = "actual", adjustmentReason = "فاتورة وصلت متأخرة" });
    }

    /// <summary>المخزون = تكلفة السيارة إن كانت متاحة وإلا لا شيء؛ تكلفة المبيعات = الأساس + ما يُتوقع أن يكون عليها.</summary>
    private static async Task AssertBooksAsync(Client api, Car car, decimal expectedCogs)
    {
        var v = (await api.Get($"/vehicles/{car.Id}")).Data!;
        var inStock = v["status"].S() == "available" ? v["totalCost"].D() : 0;
        Assert.Equal(car.StockBase + inStock, await BalanceAsync(api, Inventory));
        Assert.Equal(car.CogsBase + expectedCogs, await BalanceAsync(api, Cogs));
        await AssertBalancedAsync(api);
    }

    // ───────── العيب (ج): مرتجع فاتورة البيع يعيد السيارة بتكلفتها وقت البيع ─────────
    [Fact]
    public async Task Invoice_return_brings_the_car_back_at_its_sale_cost_and_late_charges_are_not_counted_twice()
    {
        var api = await NewTenantAsync();
        var car = await SeedCarAsync(api);
        var sale = await api.PostInvoice(Sale("posted", car.Id, 50000));
        Assert.Equal(201, sale.Status);
        Assert.Equal(40000, sale.Data!["totalCost"].D());
        Assert.Equal(201, (await LateChargeAsync(api, car.Id, 300)).Status);
        Assert.Equal(40300, (await api.Get($"/vehicles/{car.Id}")).Data!["totalCost"].D());
        await AssertBooksAsync(api, car, 40300);

        var ret = await api.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "عيب مصنعي" });
        Assert.Equal(200, ret.Status);
        Assert.Equal(40000, ret.Data!["totalCost"].D());           // المرتجع بتكلفة البيع المحفوظة
        var v = (await api.Get($"/vehicles/{car.Id}")).Data!;
        Assert.Equal("available", v["status"].S());
        Assert.Equal(40000, v["totalCost"].D());                     // مصروف ما بعد البيع خرج من تكلفتها
        await AssertBooksAsync(api, car, 300);                        // وبقي على تكلفة المبيعات وحده

        // بيعها من جديد: تكلفة المبيعات 40000 لا 40300 (المصروف المتأخر لا يُحسب مرتين)
        var resale = await api.PostInvoice(Sale("posted", car.Id, 52000));
        Assert.Equal(201, resale.Status);
        Assert.Equal(40000, resale.Data!["totalCost"].D());
        await AssertBooksAsync(api, car, 40300);

        // مرتجع ثانٍ للفاتورة الأصلية مرفوض، وتكلفة البيع الأولى لم تتغير
        Assert.Equal(409, (await api.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "x" })).Status);
        Assert.Equal(40000, (await api.Get(await api.InvoicePath(sale.Data["id"].S()))).Data!["totalCost"].D());
    }

    // ───────── العيب (أ): تعديل/حذف فاتورة بيع مرحّلة بعد مصروف ما بعد البيع ─────────
    [Fact]
    public async Task A_posted_sale_with_a_late_charge_is_corrected_by_a_return_not_by_editing_or_deleting()
    {
        var api = await NewTenantAsync();
        var car = await SeedCarAsync(api);
        var sale = await api.PostInvoice(Sale("posted", car.Id, 50000));
        Assert.Equal(201, sale.Status);
        Assert.Equal(201, (await LateChargeAsync(api, car.Id, 300)).Status);
        await AssertBooksAsync(api, car, 40300);

        // التعديل يعيد ترحيلها بتكلفة السيارة الحالية (40300) فوق المصروف المقيَّد: ممنوع، ولا أثر على الدفاتر ولا على تكلفة البيع المحفوظة
        var path = await api.InvoicePath(sale.Data!["id"].S());
        Assert.Equal(409, (await api.Put(path, Sale("posted", car.Id, 51000))).Status);
        Assert.Equal(409, (await api.Delete(path)).Status);
        var kept = (await api.Get(path)).Data!;
        Assert.Equal(40000, kept["totalCost"].D()); Assert.Equal("posted", kept["status"].S());
        Assert.Equal("sold", (await api.Get($"/vehicles/{car.Id}")).Data!["status"].S());
        await AssertBooksAsync(api, car, 40300);

        // التصحيح بمرتجع: السيارة تعود بتكلفة البيع والمصروف المتأخر يبقى على تكلفة المبيعات مرة واحدة
        Assert.Equal(200, (await api.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "تصحيح السعر" })).Status);
        await AssertBooksAsync(api, car, 300);

        // فاتورة بيع بلا مصروف بعد البيع تبقى قابلة للتعديل وبتكلفتها وقت البيع
        var other = await SeedCarAsync(api);
        var otherSale = await api.PostInvoice(Sale("posted", other.Id, 50000));
        var edited = await api.Put(await api.InvoicePath(otherSale.Data!["id"].S()), Sale("posted", other.Id, 51000));
        Assert.Equal(200, edited.Status);
        Assert.Equal(40000, edited.Data!["totalCost"].D());
        await AssertBalancedAsync(api);
    }

    // ───────── العيب (ب): ترحيل مسودة بيع بعد تغيّر تكلفة السيارة ─────────
    [Fact]
    public async Task Posting_a_sales_draft_takes_the_car_cost_at_posting_and_leaves_nothing_in_inventory()
    {
        var api = await NewTenantAsync();
        var car = await SeedCarAsync(api);
        var draft = await api.PostInvoice(Sale("draft", car.Id, 50000));
        Assert.Equal(201, draft.Status);
        Assert.Equal(201, (await LateChargeAsync(api, car.Id, 500)).Status); // السيارة متاحة: يدخل مخزونها
        Assert.Equal(40500, (await api.Get($"/vehicles/{car.Id}")).Data!["totalCost"].D());
        await AssertBooksAsync(api, car, 0);

        var posted = await api.Post(await api.InvoicePath(draft.Data!["id"].S(), "/post"));
        Assert.Equal(200, posted.Status);
        Assert.Equal(40500, posted.Data!["totalCost"].D());
        Assert.Equal(40500, posted.Data["vehicleLines"]!.AsArray()[0]!["unitCost"].D()); // محفوظة على السطر
        Assert.Equal(50000, posted.Data["subtotal"].D());                               // أرقام العميل لم تتغير
        await AssertBooksAsync(api, car, 40500);                                        // لا يبقى في المخزون شيء لسيارة مباعة
    }

    [Fact]
    public async Task A_margin_scheme_draft_whose_vat_would_change_with_the_new_cost_must_be_saved_again()
    {
        var api = await NewTenantAsync();
        Assert.Equal(200, (await api.Put("/carshowroomsettings", new { saleFeesBorneBy = "customer", reservationDays = 7, standardVatRate = 15, marginBasis = "total_cost" })).Status);
        var car = await SeedCarAsync(api, "profit_margin_15");
        var draft = await api.PostInvoice(Sale("draft", car.Id, 50000));
        Assert.Equal(201, draft.Status);
        var vatBefore = draft.Data!["vatTotal"].D();
        Assert.Equal(201, (await LateChargeAsync(api, car.Id, 500)).Status);

        // الضريبة على الهامش (السعر − التكلفة الكلية) تتغير: لا تُغيَّر أرقام الفاتورة بصمت
        var path = await api.InvoicePath(draft.Data["id"].S());
        Assert.Equal(409, (await api.Post(path + "/post")).Status);
        Assert.Equal("draft", (await api.Get(path)).Data!["status"].S());
        Assert.Equal("available", (await api.Get($"/vehicles/{car.Id}")).Data!["status"].S());
        await AssertBooksAsync(api, car, 0);

        // إعادة الحفظ تحسب الضريبة والتكلفة من جديد ثم تُرحَّل
        var saved = await api.Put(path, Sale("posted", car.Id, 50000));
        Assert.Equal(200, saved.Status);
        Assert.Equal(40500, saved.Data!["totalCost"].D());
        Assert.True(saved.Data["vatTotal"].D() < vatBefore);
        await AssertBooksAsync(api, car, 40500);
    }
}
