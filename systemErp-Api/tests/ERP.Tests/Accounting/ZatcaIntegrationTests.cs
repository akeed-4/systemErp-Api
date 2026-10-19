using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الربط مع هيئة الزكاة (المرحلة الثانية) أمام بديل محلي للهيئة يتحقق من التجزئة والتوقيع: الربط برمز التحقق،
/// خيار «إرسال للهيئة» من شاشة الفاتورة، سلسلة الفواتير، والرفض وانقطاع الاتصال.
/// </summary>
[Collection("api")]
public class ZatcaIntegrationTests : TestBase
{
    public ZatcaIntegrationTests(ErpFactory f) : base(f) { }

    private static object Config(bool autoSubmit = false, string? street = "طريق الملك فهد") => new
    {
        isEnabled = false, environment = "sandbox", autoSubmitOnInvoiceSave = autoSubmit,
        street, buildingNumber = "1234", district = "العليا", postalCode = "12345",
    };

    private async Task<(Client Api, Guid Product)> OnboardedAsync(bool autoSubmit = false)
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 100);
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", Config(autoSubmit))).Status);
        var onboard = await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp });
        Assert.Equal(200, onboard.Status);
        return (api, product);
    }

    private static object Simplified(Guid product, bool? send, string buyer = "عميل نقدي") => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", partyName = buyer, sendToZatca = send,
        items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } },
    };

    private static object Standard(Guid product, bool? send) => new
    {
        kind = "sales", invoiceType = "tax_invoice", paymentMethod = "cash", partyName = "شركة المشتري", partyVatNumber = "399999999800003", sendToZatca = send,
        partyStreet = "شارع التحلية", partyBuildingNo = "4321", partyDistrict = "السليمانية", partyCity = "الرياض", partyPostalCode = "54321",
        items = new[] { new { itemId = product, quantity = 2, unitPrice = 50, vatRate = 15 } },
    };

    private static string Icv(JsonNode invoice)
        => FakeZatcaApiClient.Between(invoice["zatcaUblXml"].S(), "<cbc:ID>ICV</cbc:ID><cbc:UUID>", "</cbc:UUID>");

    [Fact]
    public async Task Onboarding_needs_the_national_address_and_a_valid_otp_then_issues_the_certificate()
    {
        var api = await NewTenantAsync();
        // بلا عنوان وطني: يُرفض قبل الاتصال بالهيئة
        await api.Put("/company/ZatcaConfig", Config(street: null));
        Assert.Equal(400, (await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp })).Status);

        await api.Put("/company/ZatcaConfig", Config());
        Assert.Equal(400, (await api.Post("/company/ZatcaOnboard", new { otp = "" })).Status);
        Assert.Equal(400, (await api.Post("/company/ZatcaOnboard", new { otp = "000000" })).Status); // رمز خاطئ ترفضه الهيئة
        Assert.Equal("not_enrolled", (await api.Get("/company")).Data!["zatcaConfig"]!["complianceStatus"].S());

        var onboard = await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp });
        Assert.Equal(200, onboard.Status);
        var config = onboard.Data!["zatcaConfig"]!;
        Assert.Equal("compliant", config["complianceStatus"].S());
        Assert.Equal("phase2", config["phase"].S());
        Assert.True(config["isEnabled"]!.GetValue<bool>());
        Assert.NotNull(config["onboardedAt"]);
        // الأسرار لا تخرج من السرفر
        Assert.Null(config["privateKeyPem"]); Assert.Null(config["secretKey"]); Assert.Null(config["binarySecurityToken"]);
        Assert.True((await api.Post("/company/ZatcaTest")).Data!["success"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Screen_flag_reports_a_simplified_invoice_and_clears_a_tax_invoice_in_one_chain()
    {
        var (api, product) = await OnboardedAsync();

        // بلا الخيار: الفاتورة تُرحَّل ولا تُرسل
        var silent = await api.PostInvoice(Simplified(product, send: false));
        Assert.Equal(201, silent.Status);
        Assert.Equal("not_submitted", silent.Data!["zatcaStatus"].S());
        Assert.Null(silent.Data["zatcaHash"]);

        var first = await api.PostInvoice(Simplified(product, send: true));
        Assert.Equal(201, first.Status);
        Assert.Equal("posted", first.Data!["status"].S());
        Assert.Equal("reported", first.Data["zatcaStatus"].S());
        Assert.Equal("1", Icv(first.Data));
        Assert.False(string.IsNullOrEmpty(first.Data["zatcaHash"].S()));
        // رمز QR للمرحلة الثانية يحمل التجزئة والتوقيع والمفتاح (أطول من وسوم المرحلة الأولى الخمسة)
        Assert.True(Convert.FromBase64String(first.Data["zatcaQrCode"].S()).Length > 250);

        // الضريبية تُعتمد وتُحفظ نسختها المختومة، وتجزئة السابقة تربطها بالفاتورة التي قبلها
        var second = await api.PostInvoice(Standard(product, send: true));
        Assert.Equal(201, second.Status);
        Assert.Equal("cleared", second.Data!["zatcaStatus"].S());
        Assert.Equal("2", Icv(second.Data));
        Assert.Equal(first.Data["zatcaHash"].S(), second.Data["zatcaPih"].S());
        Assert.Contains("name=\"0100000\"", second.Data["zatcaUblXml"].S());

        // الإرسال اليدوي لاحقاً لفاتورة لم تُرسل، وإعادة إرساله لا تكرر الفاتورة في السلسلة
        var manual = await api.Post(await api.InvoicePath(silent.Data["id"].S(), "/SubmitZatca"));
        Assert.True(manual.Data!["success"]!.GetValue<bool>());
        Assert.Equal("reported", manual.Data["status"].S());
        var again = await api.Post(await api.InvoicePath(silent.Data["id"].S(), "/SubmitZatca"));
        Assert.True(again.Data!["success"]!.GetValue<bool>());
        Assert.Equal("3", Icv((await api.Get(await api.InvoicePath(silent.Data["id"].S(), ""))).Data!));

        // المرتجع إشعار دائن يتبع نوع فاتورته، ويُرسل بإعداد المنشأة حين لا تحدد الشاشة
        await api.Put("/company/ZatcaConfig", new { isEnabled = true, environment = "sandbox", autoSubmitOnInvoiceSave = true, street = "طريق الملك فهد", buildingNumber = "1234", district = "العليا", postalCode = "12345", phase = "phase2" });
        var credit = await api.PostReturn(new { originalInvoiceId = first.Data["id"].S(), returnReason = "إرجاع بضاعة" });
        Assert.Equal(200, credit.Status);
        Assert.Equal("reported", credit.Data!["zatcaStatus"].S());
        Assert.Contains(">381<", credit.Data["zatcaUblXml"].S());
        Assert.Contains("name=\"0200000\"", credit.Data["zatcaUblXml"].S());
        Assert.Equal("4", Icv(credit.Data));
    }

    [Fact]
    public async Task Rejection_and_outage_keep_the_invoice_posted_with_the_reason_and_allow_resending()
    {
        var (api, product) = await OnboardedAsync();

        // رفض الهيئة: الفاتورة تبقى مرحّلة وسبب الرفض محفوظ عليها
        var rejected = await api.PostInvoice(Simplified(product, send: true, buyer: FakeZatcaApiClient.RejectedBuyer));
        Assert.Equal(201, rejected.Status);
        Assert.Equal("posted", rejected.Data!["status"].S());
        Assert.Equal("rejected", rejected.Data["zatcaStatus"].S());
        Assert.Contains("BR-KSA-TEST", rejected.Data["zatcaValidationMessages"].S());

        // تحذير: مقبولة مع حفظ التحذير
        var warned = await api.PostInvoice(Simplified(product, send: true, buyer: FakeZatcaApiClient.WarnedBuyer));
        Assert.Equal("warning", warned.Data!["zatcaStatus"].S());
        Assert.Contains("BR-KSA-WARN", warned.Data["zatcaValidationMessages"].S());
        // المرفوضة حلقة في السلسلة: التالية تحمل تجزئتها
        Assert.Equal(rejected.Data["zatcaHash"].S(), warned.Data["zatcaPih"].S());

        // انقطاع الاتصال: لا حكم، فالسلسلة لا تتقدّم والفاتورة تُعاد لاحقاً
        FakeZatcaApiClient.Instance.Down = true;
        JsonNode offline;
        try
        {
            offline = (await api.PostInvoice(Simplified(product, send: true))).Data!;
        }
        finally
        {
            FakeZatcaApiClient.Instance.Down = false;
        }
        Assert.Equal("posted", offline["status"].S());
        Assert.Equal("not_submitted", offline["zatcaStatus"].S());
        Assert.False(string.IsNullOrEmpty(offline["zatcaValidationMessages"].S()));

        var resent = await api.Post(await api.InvoicePath(offline["id"].S(), "/SubmitZatca"));
        Assert.True(resent.Data!["success"]!.GetValue<bool>());
        var stored = (await api.Get(await api.InvoicePath(offline["id"].S(), ""))).Data!;
        Assert.Equal("reported", stored["zatcaStatus"].S());
        Assert.Equal("3", Icv(stored));
        Assert.Equal(warned.Data["zatcaHash"].S(), stored["zatcaPih"].S());
        Assert.Null(stored["zatcaValidationMessages"]);
    }

    [Fact]
    public async Task Each_user_links_himself_and_sends_with_his_own_unit_and_chain()
    {
        var (owner, product) = await OnboardedAsync();
        var email = $"rep{Guid.NewGuid():N}@test.com";
        var created = await owner.Post("/users", new { name = "مندوب", email, phone = "1", password = "Passw0rd!", role = "sales_rep" });
        Assert.Equal(200, created.Status);
        var repId = created.Data!["id"].S();
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        // المالك أرسل فاتورة: عدّاد وحدته 1
        Assert.Equal("1", Icv((await owner.PostInvoice(Simplified(product, send: true))).Data!));

        // مستخدم غير مربوط: فاتورته تُرحَّل ولا تُرسل، والسبب محفوظ عليها
        var unsent = await rep.PostInvoice(Simplified(product, send: true));
        Assert.Equal(201, unsent.Status);
        Assert.Equal("not_submitted", unsent.Data!["zatcaStatus"].S());
        Assert.Contains("مندوب", unsent.Data["zatcaValidationMessages"].S());
        Assert.Empty((await rep.Get("/company/ZatcaUnits")).Data!.AsArray());

        // المستخدم العادي يربط نفسه مباشرة، ولا يربط غيره
        var ownerId = (await owner.Get("/auth/me")).Data!["id"].S();
        Assert.Equal(403, (await rep.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp, userId = ownerId })).Status);
        Assert.Equal(200, (await rep.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp })).Status);

        // يرى وحدته فقط، والمالك يرى الوحدتين
        var mine = (await rep.Get("/company/ZatcaUnits")).Data!.AsArray();
        Assert.Single(mine);
        Assert.Equal(repId, mine[0]!["userId"].S());
        Assert.True(mine[0]!["isMine"]!.GetValue<bool>());
        Assert.Null(mine[0]!["privateKeyPem"]); Assert.Null(mine[0]!["secretKey"]);
        Assert.Equal(2, (await owner.Get("/company/ZatcaUnits")).Data!.AsArray().Count);

        // يرسل بوحدته: سلسلته مستقلة تبدأ من 1
        var resent = await rep.Post(await rep.InvoicePath(unsent.Data["id"].S(), "/SubmitZatca"));
        Assert.True(resent.Data!["success"]!.GetValue<bool>());
        var stored = (await rep.Get(await rep.InvoicePath(unsent.Data["id"].S(), ""))).Data!;
        Assert.Equal("1", Icv(stored));
        Assert.Equal("2", Icv((await owner.PostInvoice(Simplified(product, send: true))).Data!));

        // المالك يوقف ربط المستخدم فيتوقف إرساله، ثم يعيد ربطه فتكمل سلسلته
        var unitId = mine[0]!["id"].S();
        Assert.Equal(200, (await owner.Delete($"/company/ZatcaUnits/{unitId}")).Status);
        Assert.Equal("not_submitted", (await rep.PostInvoice(Simplified(product, send: true))).Data!["zatcaStatus"].S());
        Assert.Equal(200, (await owner.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp, userId = repId })).Status);
        Assert.Equal("2", Icv((await rep.PostInvoice(Simplified(product, send: true))).Data!));
    }

    [Fact]
    public async Task Without_onboarding_the_flag_does_not_block_the_invoice()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10);
        var invoice = await api.PostInvoice(Simplified(product, send: true));
        Assert.Equal(201, invoice.Status);
        Assert.Equal("posted", invoice.Data!["status"].S());
        Assert.Equal("not_submitted", invoice.Data["zatcaStatus"].S());
        Assert.False((await api.Post(await api.InvoicePath(invoice.Data["id"].S(), "/SubmitZatca"))).Data!["success"]!.GetValue<bool>());

        // مفعَّل بلا ربط: السبب يُحفظ على الفاتورة
        await api.Put("/company/ZatcaConfig", new { isEnabled = true, environment = "sandbox" });
        var failed = await api.Post(await api.InvoicePath(invoice.Data["id"].S(), "/SubmitZatca"));
        Assert.False(failed.Data!["success"]!.GetValue<bool>());
        Assert.False(string.IsNullOrEmpty((await api.Get(await api.InvoicePath(invoice.Data["id"].S(), ""))).Data!["zatcaValidationMessages"].S()));
    }
}
