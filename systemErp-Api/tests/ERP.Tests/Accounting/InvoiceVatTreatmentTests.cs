using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// المعالجة الضريبية للفاتورة كلها (صفري / معفى / صادرات) تجعل كل أسطرها بنسبة 0 بتصنيف المعالجة وسببها، ولو كانت أصنافها أساسية،
/// في فواتير التجارة والمعارض بيعاً وشراءً؛ وتحتاج صلاحية تجاوز التصنيف الضريبي، والمرتجع يرثها من أصله.
/// </summary>
[Collection("api")]
public class InvoiceVatTreatmentTests : TestBase
{
    public InvoiceVatTreatmentTests(ErpFactory f) : base(f) { }

    private static object Line(string name, decimal price, decimal rate = 15) => new { itemName = name, quantity = 1, unitPrice = price, vatRate = rate };

    private static object Sale(string treatment, string? reason, params object[] items)
        => new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", vatTreatment = treatment, vatTreatmentReasonCode = reason, items };

    private static IEnumerable<JsonNode> Items(Res r) => r.Data!["items"]!.AsArray().Select(i => i!);

    [Fact]
    public async Task Zero_rated_exempt_and_export_force_every_standard_line_to_zero_vat()
    {
        var api = await NewTenantAsync();

        var zero = await api.PostInvoice(Sale("zero_rated", "VATEX-SA-35", Line("أ", 100), Line("ب", 50)));
        Assert.Equal(201, zero.Status);
        Assert.Equal("zero_rated", zero.Data!["vatTreatment"].S());
        Assert.All(Items(zero), i => { Assert.Equal("zero_rated", i["vatCategory"].S()); Assert.Equal(0, i["vatRate"].D()); Assert.Equal("VATEX-SA-35", i["vatExemptionReasonCode"].S()); });
        Assert.Equal(0, zero.Data["vatTotal"].D()); Assert.Equal(150, zero.Data["grandTotal"].D());

        var export = await api.PostInvoice(Sale("export", null, Line("تصدير", 200)));
        Assert.Equal(201, export.Status);
        Assert.Equal("VATEX-SA-32", Items(export).Single()["vatExemptionReasonCode"].S()); // سبب التصدير الافتراضي
        Assert.Equal(200, export.Data!["grandTotal"].D());

        var exempt = await api.PostInvoice(Sale("exempt", "VATEX-SA-29", Line("خدمة مالية", 80)));
        Assert.Equal(201, exempt.Status);
        Assert.Equal("exempt", Items(exempt).Single()["vatCategory"].S());

        // المبيعات الصفرية/المعفاة تحتاج سبباً مطابقاً، والصادرات سبب تصدير
        Assert.Equal(400, (await api.PostInvoice(Sale("zero_rated", null, Line("أ", 10)))).Status);
        Assert.Equal(400, (await api.PostInvoice(Sale("exempt", "VATEX-SA-35", Line("أ", 10)))).Status);
        Assert.Equal(400, (await api.PostInvoice(Sale("export", "VATEX-SA-35", Line("أ", 10)))).Status);

        // الإقرار: الصادرات في خانتها، ولا ضريبة مخرجات
        var from = DateTime.UtcNow.Date.AddDays(-1).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");
        var vat = (await api.Get($"/reports/VatReturn?from={from}&to={to}")).Data!;
        Assert.Equal(150, vat["zeroRatedSales"].D()); Assert.Equal(200, vat["exportSales"].D()); Assert.Equal(80, vat["exemptSales"].D());
        Assert.Equal(0, vat["outputVat"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Exempt_purchase_needs_no_reason_and_posts_no_input_vat_and_its_return_inherits_it()
    {
        var api = await NewTenantAsync();
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "cash", status = "posted", vatTreatment = "exempt",
            items = new[] { Line("إيجار", 300) },
        });
        Assert.Equal(201, purchase.Status);
        Assert.Equal(0, purchase.Data!["vatTotal"].D()); Assert.Equal(300, purchase.Data["grandTotal"].D());

        var journal = (await api.Get($"/journalentries/{purchase.Data["journalEntryId"].S()}")).Data!;
        Assert.DoesNotContain(journal["lines"]!.AsArray(), l => l!["accountCode"].S() == "1131"); // لا ضريبة مدخلات

        var ret = await api.PostReturn(new { originalInvoiceId = purchase.Data["id"].S(), returnReason = "x" });
        Assert.Equal(200, ret.Status);
        Assert.Equal("exempt", ret.Data!["vatTreatment"].S());
        Assert.Equal(0, ret.Data["vatTotal"].D());
    }

    [Fact]
    public async Task Showroom_vehicle_invoices_follow_the_document_treatment()
    {
        var api = await NewTenantAsync();
        var (supplier, _) = await SeedSupplierAsync(api);
        var purchase = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier, status = "posted", vatTreatment = "zero_rated",
            vehicleLines = new[]
            {
                new { vin = Client.NewVin('3'), brandNameAr = "تويوتا", modelNameAr = "كامري", trimNameAr = "GLE", year = 2025,
                    colorExterior = "أبيض", colorInterior = "بيج", vatMode = "standard_15", unitPrice = 1000m, discount = 0m },
            },
        });
        Assert.Equal(201, purchase.Status);
        Assert.Equal(0, purchase.Data!["vatTotal"].D()); Assert.Equal(1000, purchase.Data["grandTotal"].D());

        var vehicle = (await api.Get("/vehicles")).Data!["items"]!.AsArray()[0]!;
        var sale = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted", vatTreatment = "export",
            vehicleLines = new[] { new { vehicleId = vehicle["id"].S(), unitPrice = 1500m, discount = 0m, vatMode = "standard_15" } },
        });
        Assert.Equal(201, sale.Status);
        Assert.Equal(0, sale.Data!["vatTotal"].D()); Assert.Equal(1500, sale.Data["grandTotal"].D());
        Assert.Equal("VATEX-SA-32", Items(sale).Single()["vatExemptionReasonCode"].S());
    }

    [Fact]
    public async Task Choosing_a_non_standard_treatment_needs_the_tax_override_permission()
    {
        var owner = await NewTenantAsync();
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        Assert.Equal(403, (await rep.PostInvoice(Sale("export", null, Line("أ", 100)))).Status);
        Assert.Equal(201, (await rep.PostInvoice(Sale("standard", null, Line("أ", 100)))).Status);
    }
}
