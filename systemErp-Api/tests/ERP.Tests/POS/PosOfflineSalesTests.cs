using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// البيع أوف لاين في نقطة البيع: يُرحَّل عند عودة الاتصال بتاريخ بيعه وسعره المطبوع، مرة واحدة فقط مهما أُعيد إرساله،
/// نقداً فقط وخلال 12 ساعة، ولا يُرفض لنقص الرصيد (يُعلَّم للمراجعة).
/// </summary>
[Collection("api")]
public class PosOfflineSalesTests : TestBase
{
    public PosOfflineSalesTests(ErpFactory f) : base(f) { }

    private static object Offline(Guid product, string shiftId, string clientRef, DateTime issuedAt, decimal printed,
        decimal qty = 1, decimal? price = null, string pay = "CASH") => new
    {
        clientReference = clientRef,
        items = new[] { new { itemId = product, quantity = qty, offlineUnitPrice = price } },
        payments = new[] { new { paymentMethodCode = pay, amount = printed } },
        offline = new { issuedAt, receiptNumber = "OFF-POS1-0001", printedTotal = printed, shiftId },
    };

    [Fact]
    public async Task Offline_sale_syncs_once_with_its_sale_time_and_printed_price()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40, price: 100);
        await MethodAsync(api, "CASH", "cash");
        await MethodAsync(api, "MADA", "card");
        await OpenPosShiftAsync(api);
        var shiftId = (await api.Get("/pos/shifts/active")).Data!["id"].S();
        var soldAt = DateTime.UtcNow.AddHours(-3);
        var clientRef = Guid.NewGuid().ToString();

        var synced = await api.Post("/pos/transactions/checkout", Offline(product, shiftId, clientRef, soldAt, 115m));
        Assert.True(synced.Status == 201, synced.Body?.ToJsonString());
        Assert.True(synced.Data!["isOffline"]!.GetValue<bool>());
        Assert.Equal("OFF-POS1-0001", synced.Data["offlineReceiptNumber"].S());
        Assert.Equal(115, synced.Data["grandTotal"].D());
        Assert.Null(synced.Data["offlineReviewNote"]?.GetValue<string>());

        // إعادة الإرسال (انقطاع قبل وصول الرد): العملية نفسها، بلا تكرار في الوردية
        var again = await api.Post("/pos/transactions/checkout", Offline(product, shiftId, clientRef, soldAt, 115m));
        Assert.Equal(synced.Data["id"].S(), again.Data!["id"].S());
        Assert.Equal(115, (await api.Get("/pos/shifts/active")).Data!["totalGross"].D());
        Assert.Equal(9, (await api.Get($"/products/{product}")).Data!["currentStock"].D());

        // الفاتورة مؤرخة بوقت البيع لا بوقت المزامنة
        var invoice = (await api.Get(await api.InvoicePath(synced.Data["invoiceId"].S()))).Data!;
        Assert.Equal(soldAt.AddHours(3).ToString("yyyy-MM-dd HH"), DateTime.Parse(invoice["issueDate"].S()).ToString("yyyy-MM-dd HH"));

        // تغيّر السعر بعد البيع: الفاتورة بسعر الإيصال المطبوع، مع ملاحظة مراجعة
        var cheaper = await api.Post("/pos/transactions/checkout", Offline(product, shiftId, Guid.NewGuid().ToString(), DateTime.UtcNow.AddHours(-1), 92m, price: 80m));
        Assert.True(cheaper.Status == 201, cheaper.Body?.ToJsonString());
        Assert.Equal(92, cheaper.Data!["grandTotal"].D());
        Assert.Contains("80.00", cheaper.Data["offlineReviewNote"].S());
    }

    [Fact]
    public async Task Offline_sale_is_cash_only_within_12_hours_and_survives_insufficient_stock()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 1, cost: 40, price: 100);
        await MethodAsync(api, "CASH", "cash");
        await MethodAsync(api, "MADA", "card");
        Assert.Equal(200, (await api.Put("/costing/policy", new { method = "moving_average", recalculateOnNewPurchase = true, includeFreightAndCustoms = false,
            negativeInventoryPolicy = "prohibit", standardCostVarianceAccountCode = "513" })).Status);
        await OpenPosShiftAsync(api);
        var shiftId = (await api.Get("/pos/shifts/active")).Data!["id"].S();

        // أقدم من 12 ساعة، أو بطاقة، أو وردية أخرى: مرفوض
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Offline(product, shiftId, Guid.NewGuid().ToString(), DateTime.UtcNow.AddHours(-13), 115m))).Status);
        Assert.Equal(400, (await api.Post("/pos/transactions/checkout", Offline(product, shiftId, Guid.NewGuid().ToString(), DateTime.UtcNow.AddMinutes(-5), 115m, pay: "MADA"))).Status);
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", Offline(product, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), DateTime.UtcNow, 115m))).Status);

        // البيع العادي يُمنع بنقص الرصيد، والأوف لاين يُسجَّل (البضاعة خرجت فعلاً) ويُعلَّم للمراجعة
        Assert.Equal(409, (await api.Post("/pos/transactions/checkout", new
        {
            items = new[] { new { itemId = product, quantity = 3 } }, payments = new[] { new { paymentMethodCode = "CASH", amount = 345m } },
        })).Status);
        var offline = await api.Post("/pos/transactions/checkout", Offline(product, shiftId, Guid.NewGuid().ToString(), DateTime.UtcNow.AddMinutes(-30), 345m, qty: 3));
        Assert.True(offline.Status == 201, offline.Body?.ToJsonString());
        Assert.Contains("سالباً", offline.Data!["offlineReviewNote"].S());
        Assert.Equal(-2, (await api.Get($"/products/{product}")).Data!["currentStock"].D());

        var (debit, credit) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(debit, credit);
    }

    private static async Task MethodAsync(Client api, string code, string type)
    {
        var r = await api.Post("/paymentmethods", new { code, nameAr = "طريقة " + code, nameEn = code, type, linkedAccountCode = type == "cash" ? "1111" : "1112", status = "active" });
        Assert.True(r.Status == 201, r.Body?.ToJsonString());
    }
}
