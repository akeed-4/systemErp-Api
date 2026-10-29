using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>طريقة الدفع المختارة من جدول طرق الدفع تُعتمد عند حفظ المستند: تُحفظ، ويُرحَّل على حسابها، وتُرفض غير الفعّالة.</summary>
[Collection("api")]
public class DocumentPaymentMethodTests : TestBase
{
    public DocumentPaymentMethodTests(ErpFactory f) : base(f) { }

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task MethodAsync(Client api, string code, string type, string account, string status = "active")
        => Assert.Equal(201, (await api.Post("/paymentmethods", new { code, nameAr = "طريقة " + code, nameEn = code, type, linkedAccountCode = account, status })).Status);

    private static object Sale(string? code) => new { kind = "sales", invoiceType = "simplified", status = "posted", paymentMethod = "cash", paymentMethodCode = code,
        items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } } };

    [Fact]
    public async Task An_unknown_or_inactive_method_is_rejected_instead_of_silently_posting_to_the_default_account()
    {
        var api = await NewTenantAsync();
        await MethodAsync(api, "OFF", "cash", "1111", status: "inactive");
        Assert.Equal(400, (await api.PostInvoice(Sale("NOPE"))).Status);
        Assert.Equal(400, (await api.PostInvoice(Sale("OFF"))).Status);
        Assert.Equal(0, await BalanceAsync(api, "1111"));
    }

    [Fact]
    public async Task The_method_type_decides_the_invoice_payment_type()
    {
        var api = await NewTenantAsync();
        await MethodAsync(api, "MADA", "card", "1112");
        // الواجهة أرسلت «نقدي» لكن الطريقة المختارة بطاقة: النوع من الطريقة والترحيل على حسابها
        var invoice = await api.PostInvoice(Sale("MADA"));
        Assert.Equal(201, invoice.Status);
        Assert.Equal("bank_card", invoice.Data!["paymentMethod"].S());
        Assert.Equal(115, await BalanceAsync(api, "1112"));
        Assert.Equal(0, await BalanceAsync(api, "1111"));
    }

    [Fact]
    public async Task A_return_can_be_refunded_by_several_methods_scaled_to_the_server_total()
    {
        var api = await NewTenantAsync();
        await MethodAsync(api, "CASH", "cash", "1111");
        await MethodAsync(api, "BANK", "bank", "1112");
        var sale = await api.PostInvoice(Sale("CASH"));
        Assert.Equal(115, await BalanceAsync(api, "1111"));

        // التقدير في الشاشة 100 (60 + 40) والمرتجع الفعلي 115: يُوزَّع بالنسبة نفسها 69 + 46
        var ret = await api.PostReturn(new
        {
            originalInvoiceId = sale.Data!["id"].S(), returnReason = "إلغاء",
            refundSplits = new[] { new { method = "cash", paymentMethodCode = "CASH", amount = 60m }, new { method = "bank_transfer", paymentMethodCode = "BANK", amount = 40m } },
        });
        Assert.Equal(200, ret.Status);
        var splits = ret.Data!["paymentSplits"]!.AsArray();
        Assert.Equal(new[] { 69m, 46m }, splits.Select(s => s!["amount"].D()));
        Assert.Equal(new[] { "CASH", "BANK" }, splits.Select(s => s!["paymentMethodCode"].S()));
        Assert.Equal(46, await BalanceAsync(api, "1111"));
        Assert.Equal(-46, await BalanceAsync(api, "1112"));

        // طريقة موقوفة في الرد تُرفض
        Assert.Equal(400, (await api.PostReturn(new
        {
            originalInvoiceId = (await api.PostInvoice(Sale("CASH"))).Data!["id"].S(), returnReason = "إلغاء",
            refundSplits = new[] { new { method = "cash", paymentMethodCode = "NOPE", amount = 115m } },
        })).Status);
    }

    [Fact]
    public async Task A_voucher_saves_its_chosen_method_and_posts_to_the_method_account()
    {
        var api = await NewTenantAsync();
        await MethodAsync(api, "BANK-2", "bank", "1112");
        await MethodAsync(api, "LATER", "credit", "1111");
        var customer = (await api.Post("/customers", new { nameAr = "عميل", phone = "050", city = "الرياض", status = "active" })).Data!;
        object Receipt(string code, string treasury) => new { type = "receipt", amount = 80, partyName = "عميل", partyAccountCode = customer["accountCode"].S(),
            treasuryAccountCode = treasury, paymentMethod = "cash", paymentMethodCode = code };

        // الحساب المرسل (الصندوق) يُستبدل بحساب الطريقة المختارة
        var voucher = await api.Post("/vouchers", Receipt("BANK-2", "1111"));
        Assert.True(voucher.Success);
        Assert.Equal("BANK-2", voucher.Data!["paymentMethodCode"].S());
        Assert.Equal("1112", voucher.Data["treasuryAccountCode"].S());
        Assert.Equal("bank_transfer", voucher.Data["paymentMethod"].S());
        Assert.Equal(80, await BalanceAsync(api, "1112"));
        Assert.Equal(0, await BalanceAsync(api, "1111"));
        Assert.Equal("BANK-2", (await api.Get($"/vouchers/{voucher.Data["id"].S()}")).Data!["paymentMethodCode"].S());

        // السند تحصيل فوري: الطريقة الآجلة لا تصلح
        Assert.Equal(400, (await api.Post("/vouchers", Receipt("LATER", "1111"))).Status);
    }

    [Fact]
    public async Task Pos_checkout_with_configured_methods_posts_each_part_to_its_account_and_feeds_the_shift()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        await MethodAsync(api, "CASH", "cash", "1111");
        await MethodAsync(api, "MADA", "card", "1112");
        await MethodAsync(api, "LATER", "credit", "1111");
        await OpenPosShiftAsync(api);
        var cashBefore = await BalanceAsync(api, "1111");

        // 115 مستحق: مدى 70 ونقد 50، والباقي 5 من النقد
        var tx = await api.Post("/pos/transactions/checkout", new
        {
            items = new[] { new { itemId = product, quantity = 1 } },
            payments = new[] { new { paymentMethodCode = "MADA", amount = 70m }, new { paymentMethodCode = "CASH", amount = 50m } },
        });
        Assert.Equal(201, tx.Status);
        Assert.Equal("split", tx.Data!["paymentMethod"].S());
        Assert.Equal(45, tx.Data["paidCash"].D()); Assert.Equal(70, tx.Data["paidCard"].D()); Assert.Equal(5, tx.Data["changeAmount"].D());
        var payments = tx.Data["payments"]!.AsArray();
        Assert.Equal(new[] { "MADA", "CASH" }, payments.Select(p => p!["paymentMethodCode"].S()));
        Assert.Equal("card", payments[0]!["type"].S());
        Assert.Equal(70, await BalanceAsync(api, "1112"));
        Assert.Equal(cashBefore + 45, await BalanceAsync(api, "1111"));

        var shift = (await api.Get("/pos/shifts/active")).Data!;
        Assert.Equal(45, shift["totalCashSales"].D()); Assert.Equal(70, shift["totalCardSales"].D());

        // الآجل لا يُخلط بغيره، ولا يكون بلا عميل
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", new
        {
            items = new[] { new { itemId = product, quantity = 1 } },
            payments = new[] { new { paymentMethodCode = "LATER", amount = 65m }, new { paymentMethodCode = "CASH", amount = 50m } },
        })).Status);
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", new
        {
            items = new[] { new { itemId = product, quantity = 1 } },
            payments = new[] { new { paymentMethodCode = "LATER", amount = 115m } },
        })).Status);
    }
}
