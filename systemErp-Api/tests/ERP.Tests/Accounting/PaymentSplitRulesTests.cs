using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الدفع المتعدد: لكل سطر طريقة مختلفة، ورسالة واضحة بمقدار الزيادة أو النقص عن الإجمالي؛
/// وحساب طريقة الدفع (حساب الخزينة) حساب فرعي من الدليل.
/// </summary>
[Collection("api")]
public class PaymentSplitRulesTests : TestBase
{
    public PaymentSplitRulesTests(ErpFactory f) : base(f) { }

    private static async Task MethodAsync(Client api, string code, string type, string account)
        => Assert.Equal(201, (await api.Post("/paymentmethods", new { code, nameAr = "طريقة " + code, nameEn = code, type, linkedAccountCode = account, status = "active" })).Status);

    /// <summary>خدمة بـ 100 + ضريبة 15% = 115.</summary>
    private static object SplitSale(params (string Method, string Code, decimal Amount)[] splits) => new
    {
        kind = "sales", invoiceType = "simplified", status = "posted", paymentMethod = "cash", isSplitPayment = true,
        paymentSplits = splits.Select(s => new { method = s.Method, paymentMethodCode = s.Code, amount = s.Amount }).ToArray(),
        items = new[] { new { itemName = "خدمة", quantity = 1, unitPrice = 100, vatRate = 15 } },
    };

    private static string Message(Res r) => r.Body?["message"]?.ToString() ?? string.Empty;

    [Fact]
    public async Task Split_payment_rejects_a_repeated_method_and_explains_over_and_under_payment()
    {
        var api = await NewTenantAsync();
        await MethodAsync(api, "CASHBOX", "cash", "1111");
        await MethodAsync(api, "MADA", "card", "1112");

        // نقداً ثم نقداً: مرفوض باسم الطريقة المكررة
        var repeated = await api.PostInvoice(SplitSale(("cash", "CASHBOX", 60), ("cash", "CASHBOX", 55)));
        Assert.Equal(400, repeated.Status);
        Assert.Contains("CASHBOX", Message(repeated));

        // الزيادة: الرسالة تذكر المدفوع والإجمالي والفرق
        var over = await api.PostInvoice(SplitSale(("cash", "CASHBOX", 60), ("bank_card", "MADA", 65)));
        Assert.Equal(400, over.Status);
        Assert.Contains("125.00", Message(over));
        Assert.Contains("115.00", Message(over));
        Assert.Contains("10.00", Message(over));

        var under = await api.PostInvoice(SplitSale(("cash", "CASHBOX", 60), ("bank_card", "MADA", 50)));
        Assert.Equal(400, under.Status);
        Assert.Contains("5.00", Message(under));

        // طريقتان مختلفتان بمجموع مطابق: مقبول
        Assert.Equal(201, (await api.PostInvoice(SplitSale(("cash", "CASHBOX", 60), ("bank_card", "MADA", 55)))).Status);
    }

    [Fact]
    public async Task Payment_method_account_must_be_a_posting_account_from_the_chart()
    {
        var api = await NewTenantAsync();
        Assert.Equal(400, (await api.Post("/paymentmethods", new { code = "BOX", nameAr = "صندوق", type = "cash", linkedAccountCode = "111", status = "active" })).Status); // حساب رئيسي
        Assert.Equal(400, (await api.Post("/paymentmethods", new { code = "BOX", nameAr = "صندوق", type = "cash", linkedAccountCode = "9999", status = "active" })).Status); // غير موجود
        var ok = await api.Post("/paymentmethods", new { code = "BOX", nameAr = "صندوق", type = "cash", linkedAccountCode = "1111", status = "active" });
        Assert.Equal(201, ok.Status);
        Assert.Equal("الصندوق", ok.Data!["linkedAccountName"].S()); // الاسم من الدليل
    }
}
