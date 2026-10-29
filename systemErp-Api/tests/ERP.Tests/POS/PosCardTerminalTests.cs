using ERP.Tests.Infrastructure;

namespace ERP.Tests;

[Collection("api")]
public class PosCardTerminalTests : TestBase
{
    public PosCardTerminalTests(ErpFactory f) : base(f) { }

    private const string Base = "/pos/TerminalPayments";

    /// <summary>منشأة بوردية مفتوحة وطريقة «مدى» مفعّل لها الجهاز؛ المنتج 100 + 15% = 115.</summary>
    private async Task<(Client Api, Guid Product)> SetupAsync()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        Assert.Equal(201, (await api.Post("/paymentmethods", new { code = "MADA-T", nameAr = "مدى (الجهاز)", type = "card", linkedAccountCode = "1112", useCardTerminal = true, status = "active" })).Status);
        Assert.Equal(200, (await api.Post("/pos/shifts/open", new { openingCash = 0, posTerminalName = "POS-1" })).Status);
        return (api, product);
    }

    private static object Sale(Guid product, Guid? terminalPaymentId, decimal amount = 115)
        => new { items = new[] { new { itemId = product, quantity = 1 } }, paymentMethod = "card", cardTerminalPaymentId = terminalPaymentId,
            payments = new[] { new { paymentMethodCode = "MADA-T", amount } } };

    private static async Task<Guid> StartAsync(Client api, decimal amount = 115)
    {
        var r = await api.Post(Base, new { amount, paymentMethodCode = "MADA-T", provider = "geidea_webecr" });
        Assert.Equal(200, r.Status);
        Assert.Equal("pending", r.Data!["status"].S());
        Assert.False(string.IsNullOrEmpty(r.Data["reference"].S()));
        return Guid.Parse(r.Data["id"].S()!);
    }

    [Fact]
    public async Task Card_terminal_flag_is_only_allowed_on_card_methods()
    {
        var api = await NewTenantAsync();
        Assert.Equal(400, (await api.Post("/paymentmethods", new { code = "BOX", nameAr = "نقد", type = "cash", linkedAccountCode = "1111", useCardTerminal = true, status = "active" })).Status);
    }

    [Fact]
    public async Task Terminal_method_cannot_be_checked_out_without_a_terminal_approval()
    {
        var (api, product) = await SetupAsync();
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Sale(product, null))).Status);

        var id = await StartAsync(api);
        // ما زال معلّقاً: لا إتمام
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Sale(product, id))).Status);
        // موافقة بلا RRN مرفوضة
        Assert.Equal(400, (await api.Post($"{Base}/{id}/result", new { outcome = "approved" })).Status);
        var approved = await api.Post($"{Base}/{id}/result", new { outcome = "approved", rrn = "123456789012", authCode = "A1B2C3", maskedPan = "440647******1234" });
        Assert.Equal("approved", approved.Data!["status"].S());

        var tx = await api.Post("/pos/transactions/checkout", Sale(product, id));
        Assert.Equal(201, tx.Status);
        var p = (await api.Get($"{Base}/{id}")).Data!;
        Assert.Equal(tx.Data!["invoiceNumber"].S(), p["invoiceNumber"].S());
        Assert.False(string.IsNullOrEmpty(p["consumedAt"].S()));
        // لا يُستخدم مرتين، والنتيجة النهائية لا تُستبدل
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Sale(product, id))).Status);
        Assert.Equal(409, (await api.Post($"{Base}/{id}/result", new { outcome = "declined" })).Status);
    }

    [Fact]
    public async Task Approval_must_match_the_cart_total()
    {
        var (api, product) = await SetupAsync();
        var id = await StartAsync(api, 100);
        await api.Post($"{Base}/{id}/result", new { outcome = "approved", rrn = "111" });
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Sale(product, id))).Status);
    }

    [Fact]
    public async Task Unknown_result_blocks_new_attempts_and_shift_close_until_resolved()
    {
        var (api, product) = await SetupAsync();
        var id = await StartAsync(api);
        Assert.Equal("unknown", (await api.Post($"{Base}/{id}/result", new { outcome = "unknown", responseMessage = "timeout" })).Data!["status"].S());
        // «لم يُرسل» لا يصح بعد أن صارت النتيجة مجهولة
        Assert.Equal(400, (await api.Post($"{Base}/{id}/result", new { outcome = "not_sent" })).Status);

        Assert.Equal(409, (await api.Post(Base, new { amount = 115, paymentMethodCode = "MADA-T", provider = "geidea_webecr" })).Status);
        Assert.Equal(409, (await api.Post("/pos/shifts/close", new { closingCashActual = 0 })).Status);
        Assert.Single((await api.Get($"{Base}?unresolved=true")).Data!.AsArray());

        // الحسم اليدوي يتطلب ملاحظة، والاعتماد يتطلب RRN ورمز الموافقة
        Assert.Equal(400, (await api.Post($"{Base}/{id}/resolve", new { outcome = "approved", rrn = "999", authCode = "X1" })).Status);
        Assert.Equal(400, (await api.Post($"{Base}/{id}/resolve", new { outcome = "approved", rrn = "999", note = "إيصال الجهاز" })).Status);
        var resolved = await api.Post($"{Base}/{id}/resolve", new { outcome = "approved", rrn = "999", authCode = "X1", note = "إيصال الجهاز" });
        Assert.Equal("approved", resolved.Data!["status"].S());
        Assert.True(resolved.Data["resolvedManually"]!.GetValue<bool>());

        // موافقة غير مستخدمة ما زالت تمنع الإغلاق، حتى تُستخدم في بيع أو تُعكس
        Assert.Equal(409, (await api.Post("/pos/shifts/close", new { closingCashActual = 0 })).Status);
        Assert.Equal(201, (await api.Post("/pos/transactions/checkout", Sale(product, id))).Status);
        Assert.Equal(200, (await api.Post("/pos/shifts/close", new { closingCashActual = 0 })).Status);
    }

    [Fact]
    public async Task Unused_approval_can_be_reversed_and_not_sent_frees_the_shift()
    {
        var (api, _) = await SetupAsync();
        var first = await StartAsync(api);
        Assert.Equal("not_sent", (await api.Post($"{Base}/{first}/result", new { outcome = "not_sent" })).Data!["status"].S());

        var second = await StartAsync(api);
        await api.Post($"{Base}/{second}/result", new { outcome = "approved", rrn = "222" });
        Assert.Equal(409, (await api.Post("/pos/shifts/close", new { closingCashActual = 0 })).Status);
        Assert.Equal("reversed", (await api.Post($"{Base}/{second}/resolve", new { outcome = "reversed", note = "استرداد على الجهاز" })).Data!["status"].S());
        Assert.Equal(200, (await api.Post("/pos/shifts/close", new { closingCashActual = 0 })).Status);
    }
}
