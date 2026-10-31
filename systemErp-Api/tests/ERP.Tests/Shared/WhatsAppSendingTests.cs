using System.Text;
using ERP.Service.Services.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// إرسال فاتورة المبيعات PDF عبر واتساب: القالب المعتمد، صلاحية الرقم والمستند، منع التكرار ومفتاح الطلب، الفشل وإعادة الإرسال،
/// حالات التسليم من إشعارات Meta، والعزل بين المنشآت.
/// </summary>
[Collection("api")]
public class WhatsAppSendingTests : TestBase
{
    public WhatsAppSendingTests(ErpFactory f) : base(f) { }

    private static FakeWhatsAppProvider Meta => FakeWhatsAppProvider.Instance;

    private sealed record Setup(Client Api, string WabaId, string PhoneNumberId, Guid InvoiceId, string InvoiceNumber);

    /// <summary>منشأة بإضافة واتساب، رقم مربوط بقالب معتمد، وفاتورة مرحّلة لعميل جواله محلي.</summary>
    private async Task<Setup> ConnectedWithInvoiceAsync(string customerPhone = "0551234567", bool approve = true, string status = "posted")
    {
        var api = await NewTenantWithModulesAsync("accounting", "whatsapp");
        var account = Meta.NewAccount();
        Assert.Equal(200, (await api.Post("/whatsapp/connect", new { code = account.Code, wabaId = account.WabaId, phoneNumberId = account.PhoneNumberId })).Status);
        if (approve) Meta.SetTemplateStatus(account.WabaId, WhatsAppService.InvoiceTemplateName, "APPROVED");

        var product = await SeedProductAsync(api);
        var customer = await api.Post("/customers", new { nameAr = "عميل واتساب", phone = customerPhone, city = "الرياض", creditLimit = 100000, creditPeriodDays = 30, status = "active" });
        Assert.Equal(201, customer.Status);
        var inv = await api.PostInvoice(new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer.Data!["id"].G(), status,
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal(201, inv.Status);
        return new Setup(api, account.WabaId, account.PhoneNumberId, inv.Data!["id"].G(), inv.Data["invoiceNumber"].S());
    }

    private static Task<Res> SendAsync(Setup s, string? key = null, string? phone = null, bool confirm = false, string? language = null)
        => s.Api.Post($"/whatsapp/sales-invoices/{s.InvoiceId}/send", new { idempotencyKey = key ?? Guid.NewGuid().ToString("N"), phone, confirmDuplicate = confirm, language });

    private int SentTo(string phone) => Meta.Sent.Count(x => x.Message.To == phone);

    private async Task<int> PostWebhookAsync(string body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/whatsapp/webhook") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Hub-Signature-256", FakeWhatsAppProvider.Sign(body));
        return (int)(await NewHttp().SendAsync(req)).StatusCode;
    }

    private static string NewLocalMobile() => "05" + Random.Shared.Next(10_000_000, 99_999_999);

    // ---------- الإرسال ----------

    [Fact]
    public async Task A_posted_invoice_is_sent_as_a_pdf_with_the_approved_template()
    {
        var mobile = NewLocalMobile();
        var s = await ConnectedWithInvoiceAsync(mobile);
        var r = await SendAsync(s);
        Assert.Equal(200, r.Status);
        Assert.Equal("sent", r.Data!["status"].S());
        Assert.StartsWith("wamid.", r.Data["providerMessageId"].S());
        Assert.Equal("966" + mobile[1..], r.Data["recipientPhone"].S());

        var sent = Meta.Sent.Single(x => x.MessageId == r.Data["providerMessageId"].S());
        Assert.Equal(s.PhoneNumberId, sent.PhoneNumberId);
        Assert.Equal(WhatsAppService.InvoiceTemplateName, sent.Message.TemplateName);
        Assert.Equal("ar", sent.Message.Language);
        Assert.Equal(new[] { "عميل واتساب", s.InvoiceNumber, "230.00 SAR" }, sent.Message.BodyParameters);
        var pdf = Meta.Uploaded[sent.Message.MediaId];
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal($"{s.InvoiceNumber}.pdf", sent.Message.FileName);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.Content, 0, 4));

        var log = (await s.Api.Get($"/whatsapp/messages?documentType=sales-invoice&documentId={s.InvoiceId}")).Data!.AsArray();
        Assert.Single(log);
        Assert.Equal(s.InvoiceNumber, log[0]!["documentNumber"].S());
    }

    [Fact]
    public async Task Sending_requires_an_approved_template_a_posted_invoice_and_a_valid_number()
    {
        var pending = await ConnectedWithInvoiceAsync(NewLocalMobile(), approve: false);
        Assert.Equal(409, (await SendAsync(pending)).Status);

        var draft = await ConnectedWithInvoiceAsync(NewLocalMobile(), status: "draft");
        Assert.Equal(400, (await SendAsync(draft)).Status);

        var s = await ConnectedWithInvoiceAsync("050"); // جوال العميل ناقص
        Assert.Equal(400, (await SendAsync(s)).Status);
        Assert.Equal(400, (await SendAsync(s, phone: "12ab")).Status);
        Assert.Equal(400, (await SendAsync(s, phone: "+966 11 234 5678")).Status); // هاتف ثابت لا جوال
        Assert.Equal(400, (await s.Api.Post($"/whatsapp/sales-invoices/{s.InvoiceId}/send", new { phone = "0551234567" })).Status); // بلا مفتاح
        Assert.Equal(200, (await SendAsync(s, phone: "+966 55 123 4567")).Status); // رقم مُدخل يغلب بطاقة العميل

        Assert.Empty((await pending.Api.Get("/whatsapp/messages")).Data!.AsArray()); // ما رُفض قبل الإرسال لا يُسجَّل
    }

    // ---------- منع التكرار ----------

    [Fact]
    public async Task The_same_request_key_never_sends_twice_and_a_second_send_needs_confirmation()
    {
        var mobile = NewLocalMobile();
        var s = await ConnectedWithInvoiceAsync(mobile);
        var to = "966" + mobile[1..];
        var key = Guid.NewGuid().ToString("N");

        var first = await SendAsync(s, key);
        var replay = await SendAsync(s, key);
        Assert.Equal(200, replay.Status);
        Assert.Equal(first.Data!["id"].S(), replay.Data!["id"].S());
        Assert.Equal(1, SentTo(to));

        var again = await SendAsync(s);
        Assert.Equal(409, again.Status); // أُرسل للرقم نفسه: يلزم تأكيد
        Assert.Equal(1, SentTo(to));

        Assert.Equal(200, (await SendAsync(s, confirm: true)).Status);
        Assert.Equal(2, SentTo(to));
        Assert.Equal(2, (await s.Api.Get($"/whatsapp/messages?documentId={s.InvoiceId}")).Data!.AsArray().Count);
    }

    // ---------- الفشل وإعادة الإرسال ----------

    [Fact]
    public async Task A_rejected_send_is_logged_as_failed_and_can_be_resent_safely()
    {
        var mobile = NewLocalMobile();
        var s = await ConnectedWithInvoiceAsync(mobile);
        var to = "966" + mobile[1..];
        Meta.SendFailures[to] = ("131026", false);

        Assert.Equal(409, (await SendAsync(s)).Status);
        var failed = (await s.Api.Get($"/whatsapp/messages?documentId={s.InvoiceId}")).Data!.AsArray().Single()!;
        Assert.Equal("failed", failed["status"].S());
        Assert.Equal("131026", failed["errorCode"].S());
        Assert.True(failed["canResend"]!.GetValue<bool>());

        // الفاشل لا يُعدّ إرسالاً سابقاً: إرسال جديد لا يتطلب تأكيداً
        Meta.SendFailures.TryRemove(to, out _);
        var key = Guid.NewGuid().ToString("N");
        var resent = await s.Api.Post($"/whatsapp/messages/{failed["id"].S()}/resend", new { idempotencyKey = key });
        Assert.Equal(200, resent.Status);
        Assert.Equal("sent", resent.Data!["status"].S());
        Assert.Equal(failed["id"].S(), resent.Data["resendOfId"].S());
        Assert.Equal(200, (await s.Api.Post($"/whatsapp/messages/{failed["id"].S()}/resend", new { idempotencyKey = key })).Status); // المفتاح نفسه
        Assert.Equal(1, SentTo(to));

        // الناجح لا يُعاد بهذا المسار
        Assert.Equal(409, (await s.Api.Post($"/whatsapp/messages/{resent.Data["id"].S()}/resend", new { idempotencyKey = Guid.NewGuid().ToString("N") })).Status);
    }

    [Fact]
    public async Task A_lost_connection_marks_the_outcome_unknown_instead_of_retrying()
    {
        var mobile = NewLocalMobile();
        var s = await ConnectedWithInvoiceAsync(mobile);
        var to = "966" + mobile[1..];
        Meta.SendFailures[to] = ("transport", true);

        Assert.Equal(409, (await SendAsync(s)).Status);
        var unknown = (await s.Api.Get($"/whatsapp/messages?documentId={s.InvoiceId}")).Data!.AsArray().Single()!;
        Assert.Equal("unknown", unknown["status"].S());
        Assert.True(unknown["canResend"]!.GetValue<bool>());
        Meta.SendFailures.TryRemove(to, out _);
        Assert.Equal(409, (await SendAsync(s)).Status); // قد يكون وصل: الإرسال العادي يطلب تأكيداً
    }

    [Fact]
    public async Task A_revoked_token_fails_the_send_and_requires_reconnection()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        Meta.RevokeTokensOf("code-" + s.WabaId);
        Assert.Equal(409, (await SendAsync(s)).Status);
        var state = (await s.Api.Get("/whatsapp/integration")).Data!;
        Assert.Equal("token_invalid", state["status"].S());
        Assert.False(state["isConnected"]!.GetValue<bool>());
        Assert.Equal(409, (await SendAsync(s)).Status);
    }

    // ---------- حالات التسليم ----------

    [Fact]
    public async Task Delivery_statuses_from_signed_webhooks_only_move_forward()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var other = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var r = await SendAsync(s);
        var wamid = r.Data!["providerMessageId"].S();
        var otherSent = await SendAsync(other);
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        async Task<string> StatusAsync() => (await s.Api.Get($"/whatsapp/messages?documentId={s.InvoiceId}")).Data!.AsArray().Single()!["status"].S();

        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, wamid, "delivered", t + 2)));
        Assert.Equal("delivered", await StatusAsync());
        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, wamid, "sent", t + 1))); // متأخر
        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, wamid, "delivered", t + 2))); // مكرر
        Assert.Equal("delivered", await StatusAsync());
        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, wamid, "read", t + 3)));
        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, wamid, "failed", t + 4, "131026")));
        var row = (await s.Api.Get($"/whatsapp/messages?documentId={s.InvoiceId}")).Data!.AsArray().Single()!;
        Assert.Equal("read", row["status"].S());
        Assert.Equal("utility", row["pricingCategory"].S());

        // معرّف رسالة منشأة أخرى على رقم هذه المنشأة: لا أثر عبر المنشآت
        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, otherSent.Data!["providerMessageId"].S(), "failed", t + 5, "131026")));
        Assert.Equal("sent", (await other.Api.Get($"/whatsapp/messages?documentId={other.InvoiceId}")).Data!.AsArray().Single()!["status"].S());
    }

    [Fact]
    public async Task A_failed_delivery_notifies_the_sender()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var r = await SendAsync(s);
        var body = FakeWhatsAppProvider.StatusWebhook(s.WabaId, s.PhoneNumberId, r.Data!["providerMessageId"].S(), "failed",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "131026");
        Assert.Equal(200, await PostWebhookAsync(body));

        var row = (await s.Api.Get($"/whatsapp/messages?documentId={s.InvoiceId}")).Data!.AsArray().Single()!;
        Assert.Equal("failed", row["status"].S());
        Assert.Equal("131026", row["errorCode"].S());
        Assert.Equal("Recipient is not a WhatsApp user", row["errorMessage"].S());
        var notes = (await s.Api.Get("/notifications")).Data!.AsArray();
        Assert.Contains(notes, n => n!["title"].S().Contains(s.InvoiceNumber) && n["relatedDocId"].S() == s.InvoiceId.ToString());
    }

    // ---------- العزل والاستحقاق ----------

    [Fact]
    public async Task A_company_cannot_send_or_resend_or_see_another_companys_documents()
    {
        var a = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var b = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var aMobile = NewLocalMobile();
        Meta.SendFailures["966" + aMobile[1..]] = ("131026", false);
        Assert.Equal(409, (await SendAsync(a, phone: aMobile)).Status);
        var aMessage = (await a.Api.Get("/whatsapp/messages")).Data!.AsArray().Single()!["id"].S();

        Assert.Equal(404, (await b.Api.Post($"/whatsapp/sales-invoices/{a.InvoiceId}/send", new { idempotencyKey = Guid.NewGuid().ToString("N") })).Status);
        Assert.Equal(404, (await b.Api.Post($"/whatsapp/messages/{aMessage}/resend", new { idempotencyKey = Guid.NewGuid().ToString("N") })).Status);
        Assert.Empty((await b.Api.Get($"/whatsapp/messages?documentId={a.InvoiceId}")).Data!.AsArray());
    }

    [Fact]
    public async Task Sending_is_refused_by_the_server_when_the_add_on_is_withdrawn()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var plain = await NewTenantAsync();
        Assert.Equal(403, (await plain.Post($"/whatsapp/sales-invoices/{s.InvoiceId}/send", new { idempotencyKey = "k" })).Status);
        Assert.Equal(403, (await plain.Post("/whatsapp/test", new { phone = "0551234567", idempotencyKey = "k" })).Status);
    }

    [Fact]
    public async Task A_test_message_goes_through_the_same_template()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var mobile = NewLocalMobile();
        var r = await s.Api.Post("/whatsapp/test", new { phone = mobile, language = "en", idempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.Equal(200, r.Status);
        Assert.Equal("test", r.Data!["kind"].S());
        var sent = Meta.Sent.Single(x => x.MessageId == r.Data["providerMessageId"].S());
        Assert.Equal("en", sent.Message.Language);
        Assert.Equal("966" + mobile[1..], sent.Message.To);
    }

    [Fact]
    public async Task Meta_hello_world_tests_the_connection_before_the_invoice_template_is_approved()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile(), approve: false);
        var mobile = NewLocalMobile();
        Task<Res> TestAsync(string? template) => s.Api.Post("/whatsapp/test", new { phone = mobile, template, idempotencyKey = Guid.NewGuid().ToString("N") });

        Assert.Equal(409, (await TestAsync(null)).Status); // قالب الفاتورة لم يُعتمد
        Assert.Equal(409, (await TestAsync("hello_world")).Status); // ليس في الحساب

        Meta.Templates[s.WabaId]["hello_world:en_US"] = new ERP.Core.Contracts.Shared.WhatsAppTemplateInfo("hello_world", "en_US", "APPROVED", "UTILITY");
        var r = await TestAsync("hello_world");
        Assert.Equal(200, r.Status);
        Assert.Equal("hello_world", r.Data!["templateName"].S());
        Assert.Equal("en_US", r.Data["templateLanguage"].S());
        var sent = Meta.Sent.Single(x => x.MessageId == r.Data["providerMessageId"].S()).Message;
        Assert.Equal("", sent.MediaId); // بلا PDF
        Assert.Empty(sent.BodyParameters);
    }

    // ---------- نص رسالة الفاتورة المخصص ----------

    private static Task<Res> CreateTemplateAsync(Setup s, string body, string? footer = null, string language = "ar")
        => s.Api.Post("/whatsapp/invoice-templates", new { language, body, footer });

    [Fact]
    public async Task A_custom_invoice_text_is_submitted_to_meta_and_used_once_approved()
    {
        var mobile = NewLocalMobile();
        var s = await ConnectedWithInvoiceAsync(mobile);
        var created = await CreateTemplateAsync(s, "عزيزنا {customer_name}،\nفاتورتكم {invoice_number} بتاريخ {invoice_date} بمبلغ { amount }.\nمع تحيات {company_name}.",
            footer: "  للاستفسار راسلونا هنا  ");
        Assert.Equal(200, created.Status);
        Assert.Equal("PENDING", created.Data!["status"].S());
        Assert.False(created.Data["isActive"]!.GetValue<bool>());
        var name = created.Data["name"].S();
        Assert.Matches("^erp_invoice_[0-9]{12}_[0-9a-f]{4}$", name);

        var definition = Meta.Definitions[$"{name}:ar"];
        Assert.Equal("عزيزنا {{1}}،\nفاتورتكم {{2}} بتاريخ {{3}} بمبلغ {{4}}.\nمع تحيات {{5}}.", definition.BodyText);
        Assert.Equal(5, definition.BodyExamples.Count);
        Assert.Equal("للاستفسار راسلونا هنا", definition.Footer);

        // بانتظار الاعتماد: يبقى الإرسال بالقالب الافتراضي
        var first = await SendAsync(s);
        Assert.Equal(WhatsAppService.InvoiceTemplateName, Meta.Sent.Single(x => x.MessageId == first.Data!["providerMessageId"].S()).Message.TemplateName);

        Meta.SetTemplateStatus(s.WabaId, name, "APPROVED");
        var list = (await s.Api.Get("/whatsapp/invoice-templates")).Data!;
        var item = list["items"]!.AsArray().Single()!;
        Assert.Equal("APPROVED", item["status"].S());
        Assert.True(item["isActive"]!.GetValue<bool>());
        Assert.Equal("APPROVED", list["defaultStatuses"]!["ar"].S());

        var second = await SendAsync(s, confirm: true);
        Assert.Equal(200, second.Status);
        Assert.Equal(name, second.Data!["templateName"].S());
        var sent = Meta.Sent.Single(x => x.MessageId == second.Data["providerMessageId"].S()).Message;
        Assert.Equal(name, sent.TemplateName);
        Assert.Equal("عميل واتساب", sent.BodyParameters[0]);
        Assert.Equal(s.InvoiceNumber, sent.BodyParameters[1]);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", sent.BodyParameters[2]);
        Assert.Equal("230.00 SAR", sent.BodyParameters[3]);
        Assert.NotEqual("-", sent.BodyParameters[4]);

        // الإنجليزية بلا نص مخصص معتمد: القالب الافتراضي
        var en = await SendAsync(s, confirm: true, language: "en");
        Assert.Equal(WhatsAppService.InvoiceTemplateName, en.Data!["templateName"].S());
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   \n ", null)]
    [InlineData("مرحباً {customer}، فاتورتك جاهزة.", null)] // متغير غير معروف
    [InlineData("مرحباً {{customer_name}}، فاتورتك جاهزة.", null)] // صيغة Meta لا تُكتب يدوياً
    [InlineData("{customer_name} فاتورتك جاهزة.", null)] // يبدأ بمتغير
    [InlineData("فاتورتك جاهزة يا {customer_name}", null)] // ينتهي بمتغير
    [InlineData("فاتورتك جاهزة.", "تذييل بمتغير {amount}")]
    [InlineData("فاتورتك جاهزة.", "تذييل طويل جداً يتجاوز الحد المسموح به في Meta وهو ستون حرفاً فقط لا غير")]
    public async Task Invalid_texts_are_rejected_before_reaching_meta(string body, string? footer)
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        Assert.Equal(400, (await CreateTemplateAsync(s, body, footer)).Status);
        Assert.Equal(400, (await s.Api.Post("/whatsapp/invoice-templates", new { language = "fr", body = "فاتورتك جاهزة." })).Status);
        Assert.Empty((await s.Api.Get("/whatsapp/invoice-templates")).Data!["items"]!.AsArray());
    }

    [Fact]
    public async Task A_rejection_is_shown_with_its_reason_and_the_text_can_be_deleted()
    {
        var s = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var other = await ConnectedWithInvoiceAsync(NewLocalMobile());
        var created = await CreateTemplateAsync(s, "Dear {customer_name}, invoice {invoice_number} is attached.", language: "en");
        var id = created.Data!["id"].S();
        var name = created.Data["name"].S();

        // الحالة في قائمة Meta، والسبب لا يصل إلا بالإشعار
        Meta.SetTemplateStatus(s.WabaId, name, "REJECTED");
        Assert.Equal(200, await PostWebhookAsync(FakeWhatsAppProvider.TemplateWebhook(s.WabaId, name, "en", "REJECTED", "INVALID_FORMAT")));
        var item = (await s.Api.Get("/whatsapp/invoice-templates")).Data!["items"]!.AsArray().Single()!;
        Assert.Equal("REJECTED", item["status"].S());
        Assert.Equal("INVALID_FORMAT", item["reason"].S());

        Assert.Equal(404, (await other.Api.Delete($"/whatsapp/invoice-templates/{id}")).Status); // منشأة أخرى
        Assert.Equal(200, (await s.Api.Delete($"/whatsapp/invoice-templates/{id}")).Status);
        Assert.Empty((await s.Api.Get("/whatsapp/invoice-templates")).Data!["items"]!.AsArray());
        Assert.DoesNotContain(Meta.Templates[s.WabaId].Values, t => t.Name == name);
    }

    [Theory]
    [InlineData("مرحباً {customer_name}، فاتورة {invoice_number}.", "مرحباً {{1}}، فاتورة {{2}}.", "customer_name,invoice_number")]
    [InlineData("فاتورة {amount} ثم {amount} مرة أخرى.", "فاتورة {{1}} ثم {{2}} مرة أخرى.", "amount,amount")]
    [InlineData("نص بلا متغيرات.\n\n\n\nسطر", "نص بلا متغيرات.\n\nسطر", "")]
    public void Named_variables_compile_to_positional_ones_in_order(string body, string meta, string keys)
    {
        var compiled = WhatsAppInvoiceText.Compile(body, null);
        Assert.Equal(meta, compiled.MetaBody);
        Assert.Equal(keys, string.Join(',', compiled.Keys));
    }

    // ---------- تطبيع الرقم ----------

    [Theory]
    [InlineData("0551234567", "966551234567")]
    [InlineData("551234567", "966551234567")]
    [InlineData("+966 55 123 4567", "966551234567")]
    [InlineData("00966551234567", "966551234567")]
    [InlineData("9660551234567", "966551234567")]
    [InlineData("+20 100 123 4567", "201001234567")]
    [InlineData("+966112345678", null)] // ثابت سعودي
    [InlineData("050", null)]
    [InlineData("12ab", null)]
    [InlineData("", null)]
    public void Phone_numbers_are_normalized_to_e164_digits(string raw, string? expected)
        => Assert.Equal(expected, WhatsAppPhone.Normalize(raw));
}
