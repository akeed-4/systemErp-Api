using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// التصنيف الضريبي معرَّف على الصنف: فاتورة المبيعات تأخذه ونسبته من تعريفه تلقائياً (لا يختاره المستخدم)، وتحفظه لقطةً على السطر
/// فلا يغيّر تعديل الصنف لاحقاً فاتورة صدرت. تجاوزه في سطر بسبب وصلاحية مستقلة ويُسجَّل في سجل التدقيق، وهامش الربح للسيارة
/// المستعملة المؤهلة وحدها (من بطاقتها) ولا تُرسل فاتورته للهيئة.
/// </summary>
[Collection("api")]
public class ItemTaxTreatmentTests : TestBase
{
    public ItemTaxTreatmentTests(ErpFactory f) : base(f) { }

    private const string OverrideScreen = "invoice-tax-treatment-override";

    private static async Task<Guid> ItemAsync(Client api, string sku, string category = "standard", string? reason = null, decimal rate = 15)
    {
        var id = await SeedProductAsync(api, sku);
        if (category != "standard" || rate != 15) Assert.Equal(200, (await SetCategoryAsync(api, id, category, reason, rate)).Status);
        return id;
    }

    /// <summary>تعديل تصنيف الصنف كما ترسله شاشة الأصناف: كل الحقول الحالية مع التصنيف الجديد.</summary>
    private static async Task<Res> SetCategoryAsync(Client api, Guid id, string category, string? reason, decimal rate = 15)
    {
        var p = (await api.Get($"/products/{id}")).Data!.AsObject();
        return await api.Put($"/products/{id}", new
        {
            sku = p["sku"].S(), nameAr = p["nameAr"].S(), category = "C1", unit = "PCS", sellingPrice = p["sellingPrice"].D(),
            vatRate = rate, vatCategory = category, vatExemptionReasonCode = reason,
        });
    }

    private static object Line(Guid itemId, decimal price = 100, decimal vatRate = 15, string? vatCategory = null, string? reason = null, string? overrideReason = null)
        => new { itemId, quantity = 1, unitPrice = price, vatRate, vatCategory, vatExemptionReasonCode = reason, vatOverrideReason = overrideReason };

    private static object Sale(params object[] items) => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", items };

    private static JsonNode Item(Res invoice, int i = 0) => invoice.Data!["items"]!.AsArray()[i]!;

    private async Task<Client> SalesRepAsync(Client owner)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(rep.Token);
        return rep;
    }

    private static async Task GrantOverrideAsync(Client owner, string role)
    {
        var perms = (await owner.Get($"/permissions?roleId={role}")).Data!.AsArray();
        var granted = perms.Select(x => new
        {
            screenId = x!["screenId"].S(), screenNameAr = x["screenNameAr"].S(), screenNameEn = x["screenNameEn"].S(),
            canView = x["screenId"].S() == OverrideScreen || x["canView"]!.GetValue<bool>(), canCreate = x["canCreate"]!.GetValue<bool>(),
            canEdit = x["screenId"].S() == OverrideScreen || x["canEdit"]!.GetValue<bool>(),
            canDelete = x["canDelete"]!.GetValue<bool>(), canApprove = x["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = role, permissions = granted })).Status);
    }

    [Fact]
    public async Task Each_line_takes_the_tax_treatment_of_its_own_item_whatever_the_client_sends()
    {
        var api = await NewTenantAsync();
        var standard = await ItemAsync(api, "STD");
        var zero = await ItemAsync(api, "ZERO", "zero_rated", "VATEX-SA-35");
        var exempt = await ItemAsync(api, "EXM", "exempt", "VATEX-SA-29");
        var outOfScope = await ItemAsync(api, "OOS", "out_of_scope", "VATEX-SA-OOS");

        // العميل يرسل 15% أساسي لكل الأسطر (أو لا يرسل التصنيف): المعتمد تعريف كل صنف
        var sale = await api.PostInvoice(Sale(Line(standard), Line(zero), Line(exempt, vatCategory: "standard"), Line(outOfScope, vatRate: 5)));
        Assert.Equal(201, sale.Status);
        Assert.Equal(new[] { "standard", "zero_rated", "exempt", "out_of_scope" }, sale.Data!["items"]!.AsArray().Select(i => i!["vatCategory"].S()));
        Assert.Equal(new[] { 15m, 0m, 0m, 0m }, sale.Data["items"]!.AsArray().Select(i => i!["vatRate"].D()));
        Assert.Equal(new[] { 15m, 0m, 0m, 0m }, sale.Data["items"]!.AsArray().Select(i => i!["vatAmount"].D()));
        Assert.Equal(new[] { "", "VATEX-SA-35", "VATEX-SA-29", "VATEX-SA-OOS" }, sale.Data["items"]!.AsArray().Select(i => i!["vatExemptionReasonCode"].S()));
        Assert.Equal(new[] { 100m, 100m, 100m, 100m }, sale.Data["items"]!.AsArray().Select(i => i!["totalBeforeVat"].D())); // الوعاء الخاضع
        Assert.Equal(new[] { 115m, 100m, 100m, 100m }, sale.Data["items"]!.AsArray().Select(i => i!["totalAfterVat"].D()));
        Assert.Equal(15, sale.Data["vatTotal"].D()); Assert.Equal(415, sale.Data["grandTotal"].D());

        // وفي الإقرار: كل سطر في خانة تصنيفه
        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(100, vat["standardRatedSales"].D()); Assert.Equal(100, vat["zeroRatedSales"].D());
        Assert.Equal(100, vat["exemptSales"].D()); Assert.Equal(100, vat["outOfScopeSales"].D()); Assert.Equal(15, vat["outputVat"].D());
    }

    [Fact]
    public async Task A_standard_item_uses_its_own_rate_from_the_item_definition()
    {
        var api = await NewTenantAsync();
        var reduced = await ItemAsync(api, "R5", rate: 5);
        var sale = await api.PostInvoice(Sale(Line(reduced, price: 200, vatRate: 15)));
        Assert.Equal(201, sale.Status);
        Assert.Equal(5, Item(sale)["vatRate"].D()); Assert.Equal(10, Item(sale)["vatAmount"].D()); Assert.Equal(210, sale.Data!["grandTotal"].D());
    }

    [Fact]
    public async Task Changing_the_item_tax_category_does_not_change_issued_invoices_even_when_they_are_edited()
    {
        var api = await NewTenantAsync();
        var item = await ItemAsync(api, "CHG");
        var old = await api.PostInvoice(Sale(Line(item)));
        Assert.Equal(201, old.Status);
        var draft = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft", items = new[] { Line(item) } });
        Assert.Equal(201, draft.Status);

        Assert.Equal(200, (await SetCategoryAsync(api, item, "exempt", "VATEX-SA-29")).Status);

        // الفاتورة الصادرة على لقطتها
        var path = await api.InvoicePath(old.Data!["id"].S());
        var reread = await api.Get(path);
        Assert.Equal("standard", Item(reread)["vatCategory"].S()); Assert.Equal(15, Item(reread)["vatAmount"].D()); Assert.Equal(115, reread.Data!["grandTotal"].D());

        // وتعديل المسودة القديمة (تغيير الكمية) لا يعيد قراءة تعريف الصنف
        var draftPath = await api.InvoicePath(draft.Data!["id"].S());
        var edited = await api.Put(draftPath, new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "draft", items = new[] { new { itemId = item, quantity = 2, unitPrice = 100m, vatRate = 0m } } });
        Assert.Equal(200, edited.Status);
        Assert.Equal("standard", Item(edited)["vatCategory"].S()); Assert.Equal(30, Item(edited)["vatAmount"].D());

        // الفاتورة الجديدة تأخذ التعريف الجديد
        var fresh = await api.PostInvoice(Sale(Line(item)));
        Assert.Equal("exempt", Item(fresh)["vatCategory"].S()); Assert.Equal(0, Item(fresh)["vatAmount"].D()); Assert.Equal(100, fresh.Data!["grandTotal"].D());
    }

    [Fact]
    public async Task Overriding_a_line_tax_treatment_needs_the_permission_and_a_reason_and_is_audited()
    {
        var owner = await NewTenantAsync();
        var rep = await SalesRepAsync(owner);
        var item = await ItemAsync(owner, "OVR");
        var exportLine = Line(item, vatRate: 0, vatCategory: "zero_rated", reason: "VATEX-SA-32", overrideReason: "بيع تصدير لعميل خارج المملكة");

        // بلا صلاحية: مرفوض ولا تُنشأ فاتورة
        Assert.Equal(403, (await rep.PostInvoice(Sale(exportLine))).Status);

        // بلا سبب: يُتجاهل التصنيف المرسل ويبقى تعريف الصنف
        var ignored = await rep.PostInvoice(Sale(Line(item, vatRate: 0, vatCategory: "zero_rated", reason: "VATEX-SA-32")));
        Assert.Equal(201, ignored.Status);
        Assert.Equal("standard", Item(ignored)["vatCategory"].S()); Assert.Equal(15, Item(ignored)["vatAmount"].D());

        // بالصلاحية: يُقبل، ويُحفظ السبب على السطر، ويُسجَّل الأصل والجديد والسبب
        await GrantOverrideAsync(owner, "sales_rep");
        var overridden = await rep.PostInvoice(Sale(exportLine));
        Assert.Equal(201, overridden.Status);
        Assert.Equal("zero_rated", Item(overridden)["vatCategory"].S()); Assert.Equal(0, Item(overridden)["vatAmount"].D());
        Assert.Equal("بيع تصدير لعميل خارج المملكة", Item(overridden)["vatOverrideReason"].S());
        var id = overridden.Data!["id"].S();
        var logs = (await owner.Get($"/auditlogs?entityName=SalesInvoice&entityId={id}")).Data!["items"]!.AsArray();
        var log = logs.Single(x => x!["action"].S() == "INVOICE_TAX_TREATMENT_OVERRIDE")!;
        Assert.Contains("خاضع 15%", log["details"].S()); Assert.Contains("VATEX-SA-32", log["details"].S()); Assert.Contains("بيع تصدير", log["details"].S());

        // هامش الربح لا يُختار لصنف ولو بالصلاحية
        Assert.Equal(400, (await owner.PostInvoice(Sale(Line(item, vatRate: 0, vatCategory: "margin_scheme", overrideReason: "سيارة مستعملة")))).Status);
    }

    [Fact]
    public async Task Margin_scheme_is_not_an_item_category_and_comes_from_a_qualifying_used_vehicle()
    {
        var api = await NewTenantAsync();
        var item = await ItemAsync(api, "MRG");
        Assert.Equal(400, (await SetCategoryAsync(api, item, "margin_scheme", null)).Status);

        // سيارة جديدة لا تُعرَّف بهامش الربح
        Assert.Equal(400, (await api.Post("/vehicles", new { chassisNumber = Client.NewVin('7'), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2026, condition = "new",
            purchasePrice = 80000, sellingPrice = 100000, vatMode = "profit_margin_15", location = "المعرض" })).Status);

        // سيارة جديدة قياسية، ومستعملة عادية قياسية، ومستعملة مؤهلة بهامش الربح: التصنيف معروف قبل إضافتها للفاتورة
        var brandNew = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('7'), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2026, condition = "new",
            purchasePrice = 80000, sellingPrice = 100000, vatMode = "standard_15", location = "المعرض" });
        var usedStandard = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('8'), brandNameAr = "تويوتا", modelNameAr = "كامري", year = 2020, condition = "used", mileageKm = 50000,
            purchasePrice = 40000, sellingPrice = 50000, vatMode = "standard_15", location = "المعرض" });
        var usedMargin = await api.Post("/vehicles", new { chassisNumber = Client.NewVin('9'), brandNameAr = "هوندا", modelNameAr = "أكورد", year = 2020, condition = "used", mileageKm = 60000,
            purchasePrice = 80000, sellingPrice = 100000, vatMode = "profit_margin_15", location = "المعرض" });
        Assert.Equal("standard", brandNew.Data!["vatCategory"].S());
        Assert.Equal("standard", usedStandard.Data!["vatCategory"].S());
        Assert.Equal("margin_scheme", usedMargin.Data!["vatCategory"].S());

        // فاتورة بسيارتين: لكل سطر تصنيفه (القياسية 15% كاملة، والهامش على الربح مضمَّناً)
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", sendToZatca = true,
            vehicleLines = new[]
            {
                new { vehicleId = usedStandard.Data["id"].S(), unitPrice = 50000m, discount = 0m, vatMode = "standard_15" },
                new { vehicleId = usedMargin.Data["id"].S(), unitPrice = 100000m, discount = 0m, vatMode = "standard_15" },
            },
        });
        Assert.Equal(201, sale.Status);
        Assert.Equal("standard", Item(sale, 0)["vatCategory"].S()); Assert.Equal(7500, Item(sale, 0)["vatAmount"].D());
        Assert.Equal("margin_scheme", Item(sale, 1)["vatCategory"].S()); Assert.Equal(2608.70m, Item(sale, 1)["vatAmount"].D()); // 20000 × 15/115
        Assert.Equal(157500, sale.Data!["grandTotal"].D());

        // الهامش ليس صفرياً ولا معفى: ضمن المبيعات الخاضعة في الإقرار وتفصيله منفصل
        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(0, vat["zeroRatedSales"].D()); Assert.Equal(0, vat["exemptSales"].D());
        Assert.Equal(97391.30m, vat["marginSchemeSales"].D()); Assert.Equal(2608.70m, vat["marginSchemeVat"].D());
        Assert.Equal(50000 + 97391.30m, vat["standardRatedSales"].D()); Assert.Equal(7500 + 2608.70m, vat["outputVat"].D());

        // الفوترة الإلكترونية معلّقة لهامش الربح: لا تُرسل ولو طُلب، والإرسال اليدوي مرفوض
        Assert.False(sale.Data["sendToZatca"]!.GetValue<bool>());
        Assert.Equal(409, (await api.Post(await api.InvoicePath(sale.Data["id"].S(), "/SubmitZatca"))).Status);
    }
}
