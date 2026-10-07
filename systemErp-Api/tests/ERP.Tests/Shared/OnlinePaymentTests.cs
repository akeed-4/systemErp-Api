using System.Text.Json;
using System.Text.Json.Nodes;
using ERP.Core.Contracts.Shared;
using ERP.Service.Services.Shared;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>بديل Paymob في الاختبارات: يعيد نية دفع برقم طلب فريد دون أي اتصال خارجي.</summary>
public class FakePaymobClient : IPaymobClient
{
    public const string PlatformHmac = "platform-hmac-secret";
    private static long _order = 900000;
    public Task<PaymobIntention> CreateIntentionAsync(PaymobCredentials c, PaymobIntentionRequest r, CancellationToken ct = default)
    {
        var order = Interlocked.Increment(ref _order);
        return Task.FromResult(new PaymobIntention($"pi_{order}", $"cs_{order}", order, $"{c.BaseUrl}/unifiedcheckout/?publicKey={c.PublicKey}&clientSecret=cs_{order}"));
    }
}

/// <summary>إشعار Paymob كما يرسله فعلاً، موقَّعاً بـ HMAC-SHA512.</summary>
public static class PaymobCallback
{
    public static (object Body, string Hmac) Signed(long orderId, decimal amount, string secret, bool success = true, string currency = "SAR")
    {
        var obj = new JsonObject
        {
            ["id"] = 5550000 + orderId % 1000, ["pending"] = false, ["amount_cents"] = (long)(amount * 100), ["success"] = success,
            ["is_auth"] = false, ["is_capture"] = false, ["is_standalone_payment"] = true, ["is_voided"] = false, ["is_refunded"] = false,
            ["is_3d_secure"] = true, ["integration_id"] = 123, ["has_parent_transaction"] = false, ["owner"] = 42,
            ["created_at"] = "2026-09-27T12:00:00.000000", ["currency"] = currency, ["error_occured"] = !success,
            ["order"] = new JsonObject { ["id"] = orderId },
            ["source_data"] = new JsonObject { ["pan"] = "4242", ["type"] = "card", ["sub_type"] = "Visa" },
            ["data"] = new JsonObject { ["message"] = success ? "Approved" : "Do not honour" },
        };
        using var doc = JsonDocument.Parse(obj.ToJsonString());
        var hmac = PaymobHmac.Compute(doc.RootElement, secret);
        return (new JsonObject { ["type"] = "TRANSACTION", ["obj"] = obj }, hmac);
    }

    /// <summary>رقم طلب Paymob لعملية أنشأها <see cref="FakePaymobClient"/>.</summary>
    public static long OrderIdOf(JsonNode payment) => long.Parse(payment["checkoutUrl"].S().Split("clientSecret=cs_")[1]);
}

/// <summary>المدفوعات الإلكترونية: روابط دفع الفواتير، دفع نقطة البيع، واشتراكات المنشآت — بإشعارات Paymob موقَّعة.</summary>
[Collection("api")]
public class OnlinePaymentTests : TestBase
{
    public OnlinePaymentTests(ErpFactory f) : base(f) { }

    private const string TenantHmac = "tenant-hmac-secret";

    private static (object Body, string Hmac) Callback(long orderId, decimal amount, string secret, bool success = true)
        => PaymobCallback.Signed(orderId, amount, secret, success);

    private static long OrderIdOf(Res payment) => PaymobCallback.OrderIdOf(payment.Data!);

    private static async Task EnableGatewayAsync(Client api)
    {
        var r = await api.Put("/payments/GatewaySettings", new
        {
            isEnabled = true, baseUrl = "https://ksa.paymob.com", publicKey = "pk_test", secretKey = "sk_test", hmacSecret = TenantHmac,
            integrationIds = "123, 456", settlementAccountCode = "1113",
        });
        Assert.Equal(200, r.Status);
        Assert.True(r.Data!["hasSecretKey"]!.GetValue<bool>());
        Assert.Null(r.Data["secretKey"]); // الأسرار لا تُعاد أبداً
    }

    [Fact]
    public async Task Invoice_payment_link_posts_a_receipt_once_on_a_signed_webhook()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var (customer, account) = await SeedCustomerAsync(api);
        var inv = await api.Post("/invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer, status = "posted",
            items = new[] { new { itemId = product, quantity = 2, unitPrice = 100, vatRate = 15 } },
        });
        Assert.Equal(201, inv.Status);
        var invoiceId = inv.Data!["id"].S();
        Assert.Equal(409, (await api.Post("/payments/InvoiceLink", new { invoiceId })).Status); // البوابة غير مفعّلة
        await EnableGatewayAsync(api);

        var link = await api.Post("/payments/InvoiceLink", new { invoiceId });
        Assert.Equal(200, link.Status);
        Assert.Equal("pending", link.Data!["status"].S());
        Assert.Equal(230, link.Data["amount"].D());
        Assert.StartsWith("https://ksa.paymob.com/unifiedcheckout/", link.Data["checkoutUrl"].S());
        var paymentId = link.Data["id"].S();
        var order = OrderIdOf(link);

        // توقيع خاطئ: مرفوض بلا أثر
        var (body, hmac) = Callback(order, 230, TenantHmac);
        Assert.Equal(401, (await api.SendAsync(HttpMethod.Post, "/payments/paymob/webhook?hmac=bad", body, anonymous: true)).Status);
        Assert.Equal("pending", (await api.Get($"/payments/{paymentId}")).Data!["status"].S());

        // توقيع صحيح (بلا دخول، كما يرسله Paymob)
        Assert.Equal(200, (await api.SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={hmac}", body, anonymous: true)).Status);
        var paid = (await api.Get($"/payments/{paymentId}")).Data!;
        Assert.Equal("paid", paid["status"].S());
        Assert.Equal("4242", paid["cardLast4"].S());
        Assert.NotNull(paid["voucherId"]);

        // سند قبض: دائن العميل، مدين تحصيلات البوابة 1113
        Assert.Equal(0, (await api.Get($"/accounts/ByCode/{account}")).Data!["balance"].D());
        Assert.Equal(230, (await api.Get("/accounts/ByCode/1113")).Data!["balance"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        // إشعار مكرر: لا سند ثانٍ
        Assert.Equal(200, (await api.SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={hmac}", body, anonymous: true)).Status);
        Assert.Equal(230, (await api.Get("/accounts/ByCode/1113")).Data!["balance"].D());
        Assert.Equal(409, (await api.Post("/payments/InvoiceLink", new { invoiceId })).Status); // مسددة بالكامل

        // صفحة العميل العامة
        var pub = await api.SendAsync(HttpMethod.Get, $"/payments/{paymentId}/public", anonymous: true);
        Assert.Equal(200, pub.Status); Assert.Equal("paid", pub.Data!["status"].S());
    }

    [Fact]
    public async Task Pos_sale_consumes_a_paid_online_payment_exactly_once()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await EnableGatewayAsync(api);
        Assert.Equal(200, (await api.Post("/pos/shifts/open", new { openingCash = 0, posTerminalName = "POS-1" })).Status);
        var cart = new[] { new { itemId = product, quantity = 1 } };

        var pay = await api.Post("/payments/pos", new { amount = 115 });
        Assert.Equal(200, pay.Status);
        var paymentId = pay.Data!["id"].S();
        object Checkout() => new { items = cart, paymentMethod = "card", onlinePaymentId = paymentId };

        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Checkout())).Status); // لم يُدفع بعد
        var (body, hmac) = Callback(OrderIdOf(pay), 115, TenantHmac);
        Assert.Equal(200, (await api.SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={hmac}", body, anonymous: true)).Status);

        var sale = await api.Post("/pos/transactions/checkout", Checkout());
        Assert.Equal(201, sale.Status);
        Assert.Equal(115, (await api.Get("/accounts/ByCode/1113")).Data!["balance"].D()); // التحصيل على حساب البوابة لا البنك
        Assert.NotNull((await api.Get($"/payments/{paymentId}")).Data!["consumedAt"]);
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Checkout())).Status); // لا يُستخدم مرتين

        // مبلغ لا يطابق السلة
        var pay2 = await api.Post("/payments/pos", new { amount = 100 });
        var (b2, h2) = Callback(OrderIdOf(pay2), 100, TenantHmac);
        await api.SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={h2}", b2, anonymous: true);
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", new { items = cart, paymentMethod = "card", onlinePaymentId = pay2.Data!["id"].S() })).Status);
    }

    [Fact]
    public async Task Subscription_is_activated_only_by_a_matching_signed_payment()
    {
        var api = await NewTenantAsync();
        var checkout = await api.Post("/payments/subscription", new { planId = "enterprise", billingCycle = "monthly" });
        Assert.Equal(200, checkout.Status);
        Assert.Equal(999 * 1.15m, checkout.Data!["amount"].D());
        var order = OrderIdOf(checkout);

        // مبلغ مختلف في الإشعار: فشل بلا تفعيل
        var (bad, badHmac) = Callback(order, 1, FakePaymobClient.PlatformHmac);
        Assert.Equal(200, (await api.SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={badHmac}", bad, anonymous: true)).Status);
        Assert.Equal("failed", (await api.Get($"/payments/{checkout.Data["id"].S()}")).Data!["status"].S());

        var again = await api.Post("/payments/subscription", new { planId = "enterprise", billingCycle = "monthly" });
        var (body, hmac) = Callback(OrderIdOf(again), 999 * 1.15m, FakePaymobClient.PlatformHmac);
        Assert.Equal(200, (await api.SendAsync(HttpMethod.Post, $"/payments/paymob/webhook?hmac={hmac}", body, anonymous: true)).Status);
        var current = (await api.Get("/subscriptions/current")).Data!;
        Assert.Equal("enterprise", current["planType"].S());
        Assert.Equal("active", current["status"].S());
    }
}
