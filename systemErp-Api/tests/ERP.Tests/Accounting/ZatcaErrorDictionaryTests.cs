using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Services.Accounting;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// قاموس أخطاء الهيئة مطبَّقاً على الإرسال: الفحوص المحلية لا تستهلك العدّاد، رموز HTTP تحدد هل وصلت الفاتورة
/// (استُهلك ICV) أم يُعاد إرسالها بالبيانات نفسها، ورسائل التحقق تُشرح بعنوان الحالة وطريقة معالجتها.
/// </summary>
[Collection("api")]
public class ZatcaErrorDictionaryTests : TestBase
{
    public ZatcaErrorDictionaryTests(ErpFactory f) : base(f) { }

    // ---------------- القاموس نفسه ----------------

    [Fact]
    public void Codes_resolve_exactly_then_by_longest_prefix_and_warnings_never_block()
    {
        // العناوين بلغة الواجهة: تُثبَّت العربية (لغة الجهاز قد تكون الإنجليزية)
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar");
        try { ResolveCases(); }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    }

    private static void ResolveCases()
    {
        Assert.Equal("بصمة الطلب لا تطابق بصمة XML", ZatcaErrorDictionary.Resolve("INVALID-INVOICE-HASH").Title); // بلا حساسية لحالة الأحرف
        Assert.Equal("قاعدة سعودية مبنية على المعيار الأوروبي", ZatcaErrorDictionary.Resolve("BR-KSA-EN16931-11").Title); // قبل BR-KSA
        Assert.Equal("قاعدة خاصة بالمملكة", ZatcaErrorDictionary.Resolve("BR-KSA-98").Title);
        Assert.Equal("رفض من الهيئة", ZatcaErrorDictionary.Resolve("unknown-code").Title);
        Assert.Equal("رفض من الهيئة", ZatcaErrorDictionary.Resolve(null).Title);

        var warning = ZatcaErrorDictionary.Resolve("BR-KSA-31", isWarning: true);
        Assert.Equal(ZatcaIssueKind.Warning, warning.Kind);
        Assert.Equal(ZatcaChainEffect.Consumed, warning.Chain);

        Assert.Equal(ZatcaChainEffect.Consumed, ZatcaErrorDictionary.ResolveHttp(422).Chain);
        Assert.Equal(ZatcaIssueKind.Retry, ZatcaErrorDictionary.ResolveHttp(429).Kind);
        Assert.Equal(ZatcaChainEffect.Keep, ZatcaErrorDictionary.ResolveHttp(401).Chain);
        Assert.Equal(ZatcaErrorDictionary.Http[500], ZatcaErrorDictionary.ResolveHttp(502));
        Assert.All(ZatcaErrorDictionary.PreChecks.Values, i => Assert.Equal(ZatcaChainEffect.Keep, i.Chain)); // لا تصل للهيئة
    }

    [Fact]
    public void Titles_and_fixes_follow_the_ui_language_arabic_by_default()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en");
            var issue = ZatcaErrorDictionary.PreChecks["SellerAddressMissing"];
            Assert.Equal("The company national address is incomplete", issue.Title);
            Assert.StartsWith("Complete the street", issue.Fix);
            Assert.Equal("Saudi-specific rule", ZatcaErrorDictionary.Resolve("BR-KSA-98").Title);
            Assert.Equal("ZATCA service unavailable", ZatcaErrorDictionary.ResolveHttp(503).Title);

            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar");
            Assert.Equal("العنوان الوطني للمنشأة ناقص", issue.Title);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    // ---------------- عميل الهيئة: متى تُستهلك السلسلة ----------------

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static Task<ZatcaApiResult> ReportWith(HttpStatusCode status, string body)
        => new ZatcaApiClient(new StubFactory(new StubHandler(status, body)))
            .ReportAsync("https://zatca.test", "token", "secret", new ZatcaSubmission("hash", Guid.NewGuid(), "<Invoice/>"));

    [Theory]
    [InlineData(HttpStatusCode.OK, true, true)]
    [InlineData(HttpStatusCode.Accepted, true, true)]
    [InlineData(HttpStatusCode.BadRequest, true, false)]
    [InlineData((HttpStatusCode)422, true, false)]          // فشل قواعد التحقق: حكم يستهلك ICV
    [InlineData(HttpStatusCode.Unauthorized, false, false)] // مصادقة: لم تصل
    [InlineData(HttpStatusCode.TooManyRequests, false, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, false)]
    [InlineData((HttpStatusCode)418, false, false)]         // رمز غير معروف: لا تُستهلك السلسلة احتياطاً
    public async Task Http_status_decides_whether_the_invoice_reached_zatca(HttpStatusCode status, bool reached, bool accepted)
    {
        var result = await ReportWith(status, "{\"validationResults\":{\"errorMessages\":[],\"warningMessages\":[]}}");
        Assert.Equal(reached, result.Reached);
        Assert.Equal(accepted, result.Accepted);
        if (reached && !accepted) Assert.NotEmpty(result.Errors); // الرفض بلا رسائل يحمل نص الرد على الأقل
    }

    // ---------------- الإرسال من النظام ----------------

    private static object Config(string? street = "طريق الملك فهد", string environment = "sandbox", bool enabled = true) => new
    {
        isEnabled = enabled, environment, autoSubmitOnInvoiceSave = false,
        street, buildingNumber = "1234", district = "العليا", postalCode = "12345",
    };

    private async Task<(Client Api, Guid Product)> OnboardedAsync()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 100);
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", Config(enabled: false))).Status);
        Assert.Equal(200, (await api.Post("/company/ZatcaOnboard", new { otp = FakeZatcaApiClient.Otp })).Status);
        return (api, product);
    }

    private static object Simplified(Guid product, string buyer = "عميل نقدي") => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = "cash", partyName = buyer, sendToZatca = false,
        items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } },
    };

    private static string Icv(JsonNode invoice)
        => FakeZatcaApiClient.Between(invoice["zatcaUblXml"].S(), "<cbc:ID>ICV</cbc:ID><cbc:UUID>", "</cbc:UUID>");

    private static async Task<JsonNode> SubmitAsync(Client api, string invoiceId)
        => (await api.Post(await api.InvoicePath(invoiceId, "/SubmitZatca"))).Data!;

    [Fact]
    public async Task Local_checks_stop_the_submission_without_consuming_the_counter()
    {
        var (api, product) = await OnboardedAsync();
        var invoice = await api.PostInvoice(Simplified(product));
        Assert.Equal(201, invoice.Status);
        var id = invoice.Data!["id"].S();

        // عنوان المنشأة الوطني ناقص: يُوقف قبل الهيئة
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", Config(street: null) )).Status);
        var blocked = await SubmitAsync(api, id);
        Assert.False(blocked["success"]!.GetValue<bool>());
        var issue = blocked["issues"]!.AsArray().Single()!;
        Assert.Equal("SellerAddressMissing", issue["code"].S());
        Assert.False(issue["chainConsumed"]!.GetValue<bool>());
        Assert.Contains("العنوان الوطني للمنشأة ناقص", blocked["message"].S());
        Assert.DoesNotContain(FakeZatcaApiClient.Instance.Received, r => r.Uuid.ToString() == invoice.Data["uuid"].S()); // لم يصل للهيئة

        // بيئة الوحدة غير البيئة الحالية
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", Config(environment: "simulation"))).Status);
        Assert.Equal("UnitEnvMismatch", (await SubmitAsync(api, id))["issues"]![0]!["code"].S());

        // بعد التصحيح تُرسل بالعدّاد الأول: الفحوص لم تستهلكه
        Assert.Equal(200, (await api.Put("/company/ZatcaConfig", Config())).Status);
        var sent = await SubmitAsync(api, id);
        Assert.True(sent["success"]!.GetValue<bool>());
        Assert.Equal("1", Icv((await api.Get(await api.InvoicePath(id, ""))).Data!));
    }

    [Fact]
    public async Task Rejections_are_explained_and_a_rejected_invoice_is_edited_and_resent_with_a_new_uuid()
    {
        var (api, product) = await OnboardedAsync();
        var invoice = await api.PostInvoice(Simplified(product, FakeZatcaApiClient.RejectedBuyer));
        var id = invoice.Data!["id"].S();

        var rejected = await SubmitAsync(api, id);
        Assert.Equal("rejected", rejected["status"].S());
        var issue = rejected["issues"]!.AsArray().Single()!;
        Assert.Equal("BR-KSA-TEST", issue["code"].S());
        Assert.Equal("قاعدة خاصة بالمملكة", issue["title"].S());
        Assert.Equal("block", issue["kind"].S());
        Assert.True(issue["chainConsumed"]!.GetValue<bool>());
        var stored = (await api.Get(await api.InvoicePath(id, ""))).Data!;
        Assert.Contains("BR-KSA-TEST: buyer is not acceptable", stored["zatcaValidationMessages"].S());
        Assert.Contains("↳ قاعدة خاصة بالمملكة", stored["zatcaValidationMessages"].S());

        var firstUuid = stored["uuid"].S();

        // إعادة الإرسال المباشرة مسموحة (لسبب صُحّح في إعدادات المنشأة أو الشهادة): مستند جديد لدى الهيئة بمعرّف جديد
        // وعدّاد جديد؛ وهنا البيانات نفسها فتُرفض مجدداً، والشرح بلغة البرنامج (الإنجليزية عند Accept-Language: en)
        api.Language = "en";
        var english = await SubmitAsync(api, id);
        Assert.Equal("rejected", english["status"].S());
        Assert.Equal("Saudi-specific rule", english["issues"]![0]!["title"].S());
        api.Language = null;
        var second = (await api.Get(await api.InvoicePath(id, ""))).Data!;
        Assert.NotEqual(firstUuid, second["uuid"].S());
        Assert.Equal("2", Icv(second));

        // المرفوضة تُعدَّل (تصحيح المشتري) وتُعاد: تُقبل بمعرّف جديد وعدّاد جديد، ومحاولتها المرفوضة حلقة في السلسلة
        var edited = await api.Put(await api.InvoicePath(id, ""), new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", partyName = "عميل نقدي", sendToZatca = true, status = "posted",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal(200, edited.Status);
        Assert.Equal("reported", edited.Data!["zatcaStatus"].S());
        Assert.Equal(invoice.Data["invoiceNumber"].S(), edited.Data["invoiceNumber"].S()); // برقمها نفسه
        Assert.NotEqual(second["uuid"].S(), edited.Data["uuid"].S());
        Assert.Equal("3", Icv(edited.Data));
        Assert.Equal(second["zatcaHash"].S(), edited.Data["zatcaPih"].S());

        // المقبولة لا تُعدَّل ولا تُحذف، والمرفوضة لا تُحذف
        Assert.Equal(409, (await api.Put(await api.InvoicePath(id, ""), new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "cash", partyName = "عميل", status = "posted",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } },
        })).Status);
        var other = await api.PostInvoice(Simplified(product, FakeZatcaApiClient.RejectedBuyer));
        Assert.Equal("rejected", (await SubmitAsync(api, other.Data!["id"].S()))["status"].S());
        Assert.Equal(409, (await api.Delete(await api.InvoicePath(other.Data["id"].S(), ""))).Status);
    }

    [Fact]
    public async Task An_outage_is_a_retry_that_keeps_the_chain()
    {
        var (api, product) = await OnboardedAsync();
        var invoice = await api.PostInvoice(Simplified(product));
        var id = invoice.Data!["id"].S();

        FakeZatcaApiClient.Instance.Down = true;
        JsonNode offline;
        try { offline = await SubmitAsync(api, id); }
        finally { FakeZatcaApiClient.Instance.Down = false; }

        var issue = offline["issues"]!.AsArray().Single()!;
        Assert.Equal("HTTP 503", issue["code"].S());
        Assert.Equal("retry", issue["kind"].S());
        Assert.False(issue["chainConsumed"]!.GetValue<bool>());
        Assert.Contains("خدمة الهيئة غير متاحة", offline["message"].S());

        Assert.True((await SubmitAsync(api, id))["success"]!.GetValue<bool>());
        Assert.Equal("1", Icv((await api.Get(await api.InvoicePath(id, ""))).Data!));
    }
}
