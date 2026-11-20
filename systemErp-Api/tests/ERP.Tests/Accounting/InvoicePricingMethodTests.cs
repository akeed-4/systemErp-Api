using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ERP.Service.Services.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// طريقة تسعير فواتير المبيعات والمشتريات (شامل/غير شامل الضريبة) بصلاحيتين في نظام الصلاحيات نفسه: من يملك الاثنتين يختار،
/// ومن يملك واحدة لا يُقبل منه غيرها (الخادم يفرض). الطريقة تُحفظ على الفاتورة، والمرتجع يرثها، والإجماليات المحفوظة صافية دائماً،
/// وXML الهيئة للفاتورة المحفوظة ومرتجعها يطابق أرقامها. الفواتير بلا طريقة (العملاء القدامى) غير شاملة كما كانت.
/// </summary>
[Collection("api")]
public class InvoicePricingMethodTests : TestBase
{
    public InvoicePricingMethodTests(ErpFactory f) : base(f) { }

    private const string ExclusiveScreen = "invoice-vat-exclusive-pricing";
    private const string InclusiveScreen = "invoice-vat-inclusive-pricing";

    /// <summary>طلب فاتورة مبيعات؛ <paramref name="method"/> null = طلب بلا الحقل (كالعملاء القدامى).</summary>
    private static object Sale(Guid item, string? method, decimal qty = 1, decimal price = 115, decimal discount = 0, decimal invoiceDiscount = 0, string status = "posted")
    {
        var items = new[] { new { itemId = item, quantity = qty, unitPrice = price, discount, vatRate = 15 } };
        return method == null
            ? new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status, invoiceDiscount, items }
            : new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status, pricingMethod = method, invoiceDiscount, items } as object;
    }

    private async Task<Client> SalesRepAsync(Client owner)
    {
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");
        Assert.NotNull(rep.Token);
        return rep;
    }

    /// <summary>يضبط صلاحيتي التسعير لدور (وبقية صلاحياته كما هي).</summary>
    private static async Task SetPricingAsync(Client owner, string role, bool exclusive, bool inclusive)
    {
        var perms = (await owner.Get($"/permissions?roleId={role}")).Data!.AsArray();
        bool Edit(JsonNode x) => x["screenId"].S() switch
        {
            ExclusiveScreen => exclusive,
            InclusiveScreen => inclusive,
            _ => x["canEdit"]!.GetValue<bool>(),
        };
        var granted = perms.Select(x => new
        {
            screenId = x!["screenId"].S(), screenNameAr = x["screenNameAr"].S(), screenNameEn = x["screenNameEn"].S(),
            canView = x["canView"]!.GetValue<bool>(), canCreate = x["canCreate"]!.GetValue<bool>(), canEdit = Edit(x),
            canDelete = x["canDelete"]!.GetValue<bool>(), canApprove = x["canApprove"]!.GetValue<bool>(),
        }).ToList();
        Assert.Equal(200, (await owner.Post("/permissions", new { roleId = role, permissions = granted })).Status);
    }

    [Fact]
    public async Task Inclusive_sale_saves_net_totals_keeps_the_entered_price_and_posts_net_revenue()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 10);

        var sale = await api.PostInvoice(Sale(item, "inclusive"));
        Assert.True(sale.Status == 201, sale.Body?.ToJsonString());
        var d = sale.Data!;
        Assert.Equal("inclusive", d["pricingMethod"].S());
        Assert.Equal((100m, 15m, 115m), (d["subtotal"].D(), d["vatTotal"].D(), d["grandTotal"].D()));
        var line = d["items"]!.AsArray()[0]!;
        Assert.Equal(115m, line["unitPrice"].D()); // السعر كما أُدخل
        Assert.Equal((100m, 15m, 115m), (line["totalBeforeVat"].D(), line["vatAmount"].D(), line["totalAfterVat"].D()));

        var journal = await api.Get(await api.InvoicePath(d["id"].S(), "/journal"));
        Assert.Equal(200, journal.Status);
        Assert.Equal((100m, 15m, 115m), (journal.Data!["subtotal"].D(), journal.Data["vatTotal"].D(), journal.Data["grandTotal"].D()));
        Assert.Equal(journal.Data["totalDebit"].D(), journal.Data["totalCredit"].D());
        Assert.Equal(115m, journal.Data["totalDebit"].D());
    }

    [Fact]
    public async Task Invoice_without_a_method_is_exclusive_exactly_as_before()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 10);
        var sale = await api.PostInvoice(Sale(item, null, price: 100));
        Assert.Equal(201, sale.Status);
        Assert.Equal("exclusive", sale.Data!["pricingMethod"].S());
        Assert.Equal((100m, 15m, 115m), (sale.Data["subtotal"].D(), sale.Data["vatTotal"].D(), sale.Data["grandTotal"].D()));
    }

    [Fact]
    public async Task A_user_with_one_pricing_permission_can_only_use_that_method_and_both_allow_switching()
    {
        var owner = await NewTenantAsync();
        var item = await SeedProductAsync(owner, stock: 20);
        var rep = await SalesRepAsync(owner);

        // الافتراضي: غير الشامل لكل من يُصدر الفواتير، والشامل للأدوار الإدارية وحدها
        Assert.Equal(201, (await rep.PostInvoice(Sale(item, "exclusive", price: 100))).Status);
        Assert.Equal(403, (await rep.PostInvoice(Sale(item, "inclusive"))).Status);
        Assert.Equal(201, (await owner.PostInvoice(Sale(item, "inclusive"))).Status);
        Assert.Equal(201, (await owner.PostInvoice(Sale(item, "exclusive", price: 100))).Status);

        // الشامل وحده
        await SetPricingAsync(owner, "sales_rep", exclusive: false, inclusive: true);
        Assert.Equal(403, (await rep.PostInvoice(Sale(item, "exclusive", price: 100))).Status);
        Assert.Equal(403, (await rep.PostInvoice(Sale(item, null, price: 100))).Status); // بلا طريقة = غير شامل
        Assert.Equal(201, (await rep.PostInvoice(Sale(item, "inclusive"))).Status);

        // الاثنتان: يبدّل بينهما
        await SetPricingAsync(owner, "sales_rep", exclusive: true, inclusive: true);
        Assert.Equal(201, (await rep.PostInvoice(Sale(item, "exclusive", price: 100))).Status);
        Assert.Equal(201, (await rep.PostInvoice(Sale(item, "inclusive"))).Status);

        // لا شيء
        await SetPricingAsync(owner, "sales_rep", exclusive: false, inclusive: false);
        Assert.Equal(403, (await rep.PostInvoice(Sale(item, "exclusive", price: 100))).Status);
        Assert.Equal(403, (await rep.PostInvoice(Sale(item, "inclusive"))).Status);
    }

    [Fact]
    public async Task Editing_a_draft_with_its_saved_method_needs_no_new_permission_but_switching_does()
    {
        var owner = await NewTenantAsync();
        var item = await SeedProductAsync(owner, stock: 10);
        var rep = await SalesRepAsync(owner);

        var draft = await owner.PostInvoice(Sale(item, "inclusive", status: "draft"));
        Assert.Equal(201, draft.Status);
        var path = await owner.InvoicePath(draft.Data!["id"].S());

        // المندوب (غير الشامل وحده) يعدّل المسودة بطريقتها المحفوظة
        var kept = await rep.Put(path, Sale(item, "inclusive", qty: 2, status: "draft"));
        Assert.True(kept.Status == 200, kept.Body?.ToJsonString());
        Assert.Equal((200m, 30m, 230m), (kept.Data!["subtotal"].D(), kept.Data["vatTotal"].D(), kept.Data["grandTotal"].D()));

        // ومسودة غير شاملة لا يحوّلها شاملة
        var other = await owner.PostInvoice(Sale(item, "exclusive", price: 100, status: "draft"));
        Assert.Equal(403, (await rep.Put(await owner.InvoicePath(other.Data!["id"].S()), Sale(item, "inclusive", status: "draft"))).Status);
    }

    [Fact]
    public async Task Return_inherits_the_inclusive_method_and_its_proportional_discount_without_a_permission_check()
    {
        var owner = await NewTenantAsync();
        var item = await SeedProductAsync(owner, stock: 10);
        var rep = await SalesRepAsync(owner);

        // 2 × 115 شامل بخصم سطر 23 = 207: الصافي 180 والضريبة 27
        var sale = await owner.PostInvoice(Sale(item, "inclusive", qty: 2, discount: 23));
        Assert.Equal(201, sale.Status);
        Assert.Equal((180m, 27m, 207m), (sale.Data!["subtotal"].D(), sale.Data["vatTotal"].D(), sale.Data["grandTotal"].D()));

        var preview = await rep.Post("/sales-returns/return-preview", new { originalInvoiceId = sale.Data["id"].S(), lines = new[] { new { itemId = item, quantity = 1 } } });
        Assert.True(preview.Status == 200, preview.Body?.ToJsonString());
        Assert.Equal((90m, 13.5m, 103.5m), (preview.Data!["totals"]!["netTotal"].D(), preview.Data["totals"]!["vatTotal"].D(), preview.Data["totals"]!["grandTotal"].D()));

        // المندوب لا يملك الشامل، ويُرجع فاتورة شاملة: المرتجع بطريقة أصله
        var ret = await rep.PostReturn(new { originalInvoiceId = sale.Data["id"].S(), returnReason = "إرجاع", lines = new[] { new { itemId = item, quantity = 1 } } });
        Assert.True(ret.Status == 200, ret.Body?.ToJsonString());
        Assert.Equal("inclusive", ret.Data!["pricingMethod"].S());
        Assert.Equal((90m, 13.5m, 103.5m), (ret.Data["subtotal"].D(), ret.Data["vatTotal"].D(), ret.Data["grandTotal"].D()));
        Assert.Equal(115m, ret.Data["items"]!.AsArray()[0]!["unitPrice"].D());
    }

    [Fact]
    public async Task Inclusive_purchase_costs_the_stock_at_the_net_price()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 0, cost: 0);
        var supplier = await api.Post("/suppliers", new { nameAr = "مورد", phone = "050", city = "جدة", status = "active", vatNumber = Client.NewVat() });
        Assert.Equal(201, supplier.Status);

        var buy = await api.PostInvoice(new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier.Data!["id"].S(), pricingMethod = "inclusive",
            items = new[] { new { itemId = item, quantity = 2, unitPrice = 115, discount = 0, vatRate = 15 } },
        });
        Assert.True(buy.Status == 201, buy.Body?.ToJsonString());
        Assert.Equal((200m, 30m, 230m), (buy.Data!["subtotal"].D(), buy.Data["vatTotal"].D(), buy.Data["grandTotal"].D()));
        var product = (await api.Get($"/products/{item}")).Data!;
        Assert.Equal(100m, product["averageCost"].D()); // الصافي لا السعر الشامل
    }

    // ---------------- XML الهيئة للفاتورة المحفوظة (بديل الهيئة المحلي) ----------------

    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static decimal Dec(XElement? e) => decimal.Parse(e!.Value, CultureInfo.InvariantCulture);

    /// <summary>XML المحفوظ للمستند يطابق أرقامه في النظام: كل سطر (الصافي، الضريبة، سعر صافٍ، الخصم) والإجماليات.</summary>
    private static void AssertXmlMatchesInvoice(JsonNode invoice)
    {
        var xml = invoice["zatcaUblXml"].S();
        Assert.False(string.IsNullOrEmpty(xml), "لا يوجد XML محفوظ");
        var doc = XDocument.Parse(xml);
        var inclusive = invoice["pricingMethod"].S() == "inclusive";
        var items = invoice["items"]!.AsArray();
        var lines = doc.Root!.Elements(Cac + "InvoiceLine").ToList();
        Assert.Equal(items.Count, lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var x = lines[i]; var item = items[i]!;
            var qty = Dec(x.Element(Cbc + "InvoicedQuantity"));
            var price = Dec(x.Element(Cac + "Price")!.Element(Cbc + "PriceAmount"));
            var allowance = x.Elements(Cac + "AllowanceCharge").Where(a => a.Element(Cbc + "ChargeIndicator")!.Value == "false")
                .Sum(a => Dec(a.Element(Cbc + "Amount")));
            Assert.Equal(item["totalBeforeVat"].D(), Dec(x.Element(Cbc + "LineExtensionAmount")));
            Assert.Equal(item["vatAmount"].D(), Dec(x.Element(Cac + "TaxTotal")!.Element(Cbc + "TaxAmount")));
            Assert.Equal(item["totalAfterVat"].D(), Dec(x.Element(Cac + "TaxTotal")!.Element(Cbc + "RoundingAmount")));
            Assert.Equal(item["totalBeforeVat"].D(), DocumentPricing.Round(qty * price) - allowance);
            if (inclusive) Assert.True(price < item["unitPrice"].D(), $"PriceAmount {price} هو السعر الشامل");
            else Assert.Equal(item["unitPrice"].D(), price);
        }
        var totals = doc.Root.Element(Cac + "LegalMonetaryTotal")!;
        Assert.Equal(invoice["subtotal"].D(), Dec(totals.Element(Cbc + "TaxExclusiveAmount")));
        Assert.Equal(invoice["subtotal"].D(), Dec(totals.Element(Cbc + "LineExtensionAmount")));
        Assert.Equal(invoice["grandTotal"].D(), Dec(totals.Element(Cbc + "TaxInclusiveAmount")));
        Assert.Equal(invoice["grandTotal"].D(), Dec(totals.Element(Cbc + "PayableAmount")));
        Assert.All(doc.Root.Elements(Cac + "TaxTotal"), t => Assert.Equal(invoice["vatTotal"].D(), Dec(t.Element(Cbc + "TaxAmount"))));
    }

    [Fact]
    public async Task Saved_inclusive_invoice_and_its_credit_note_produce_xml_matching_the_system_totals()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 100);
        var other = await SeedProductAsync(api, "P2", stock: 100);
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", new
        {
            isEnabled = false, environment = "sandbox", autoSubmitOnInvoiceSave = false,
            street = "طريق الملك فهد", buildingNumber = "1234", district = "العليا", postalCode = "12345",
        })).Status);
        Assert.Equal(200, (await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp })).Status);

        // ضريبية بخصم سطر وخصم فاتورة شاملين وفروق تقريب (3 × 33.33)
        var invoice = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "tax_invoice", paymentMethod = "cash", partyName = "شركة المشتري", partyVatNumber = "399999999800003",
            partyStreet = "شارع التحلية", partyBuildingNo = "4321", partyDistrict = "السليمانية", partyCity = "الرياض", partyPostalCode = "54321",
            sendToZatca = true, pricingMethod = "inclusive", invoiceDiscount = 14.95m,
            items = new object[]
            {
                new { itemId = item, quantity = 2, unitPrice = 57.5m, discount = 11.5m, vatRate = 15 },
                new { itemId = other, quantity = 3, unitPrice = 33.33m, discount = 0m, vatRate = 15 },
            },
        });
        Assert.True(invoice.Status == 201, invoice.Body?.ToJsonString());
        Assert.Equal("cleared", invoice.Data!["zatcaStatus"].S());
        // النظام = 2×57.5−11.5 + 99.99 − 14.95 شاملاً
        var expected = DocumentPricing.Price(new[] { new PricedLineInput(2, 57.5m, 11.5m, 15), new PricedLineInput(3, 33.33m, 0, 15) },
            14.95m, ERP.Core.Models.Shared.InvoicePricingMethod.Inclusive);
        Assert.Equal((expected.NetTotal, expected.VatTotal, expected.GrandTotal),
            (invoice.Data["subtotal"].D(), invoice.Data["vatTotal"].D(), invoice.Data["grandTotal"].D()));
        AssertXmlMatchesInvoice(invoice.Data);

        // المرتجع يُرسل بإعداد المنشأة (لا خيار له في الطلب)
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", new
        {
            isEnabled = true, environment = "sandbox", autoSubmitOnInvoiceSave = true,
            street = "طريق الملك فهد", buildingNumber = "1234", district = "العليا", postalCode = "12345", phase = "phase2",
        })).Status);
        var credit = await api.PostReturn(new
        {
            originalInvoiceId = invoice.Data["id"].S(), returnReason = "إرجاع بضاعة",
            lines = new[] { new { itemId = item, quantity = 1 } },
        });
        Assert.True(credit.Status == 200, credit.Body?.ToJsonString());
        Assert.Equal("inclusive", credit.Data!["pricingMethod"].S());
        Assert.Contains(">381<", credit.Data["zatcaUblXml"].S());
        AssertXmlMatchesInvoice(credit.Data);
    }

    [Fact]
    public async Task Saved_exclusive_invoice_xml_is_unchanged_and_matches_the_system_totals()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api, stock: 100);
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", new
        {
            isEnabled = false, environment = "sandbox", autoSubmitOnInvoiceSave = false,
            street = "طريق الملك فهد", buildingNumber = "1234", district = "العليا", postalCode = "12345",
        })).Status);
        Assert.Equal(200, (await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp })).Status);

        var invoice = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", partyName = "عميل نقدي", sendToZatca = true, invoiceDiscount = 5m,
            items = new[] { new { itemId = item, quantity = 2, unitPrice = 50.33m, discount = 0.66m, vatRate = 15 } },
        });
        Assert.True(invoice.Status == 201, invoice.Body?.ToJsonString());
        Assert.Equal("exclusive", invoice.Data!["pricingMethod"].S());
        AssertXmlMatchesInvoice(invoice.Data);
    }
}
