using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// تغيير مراكز التكلفة بعد الترحيل وبعد الهيئة (القرار 4): القيد نفسه بالحسابات والمبالغ نفسها وأبعاد جديدة،
/// بلا حركة مخزون ولا تغيير في الفاتورة الصادرة، وبسبب إلزامي.
/// </summary>
[Collection("api")]
public class InvoiceCostCenterReassignTests : TestBase
{
    public InvoiceCostCenterReassignTests(ErpFactory f) : base(f) { }

    private static async Task<Guid> CenterAsync(Client api, string code)
        => (await api.Post("/costcenters", new { code, nameAr = "مركز " + code, nameEn = code, isActive = true })).Data!["id"].G();

    [Fact]
    public async Task Reported_invoice_moves_its_journal_to_new_cost_centers_with_identical_amounts_and_no_stock_effect()
    {
        var api = await NewTenantAsync();
        var (customer, _) = await SeedCustomerAsync(api);
        var a = await SeedProductAsync(api, "A", stock: 20);
        var b = await SeedProductAsync(api, "B", stock: 20);
        var old = await CenterAsync(api, "C1");
        var east = await CenterAsync(api, "C2");
        var west = await CenterAsync(api, "C3");
        var sale = await api.Post("/sales-invoices", new
        {
            kind = "sales", invoiceType = "simplified", paymentMethod = "credit", partyId = customer, status = "posted", costCenterId = old,
            items = new object[] { new { itemId = a, quantity = 2, unitPrice = 100, vatRate = 15 }, new { itemId = b, quantity = 3, unitPrice = 50, vatRate = 15 } },
        });
        Assert.Equal(201, sale.Status);
        var id = sale.Data!["id"].S();
        var entryId = sale.Data["journalEntryId"].S();
        var lines = sale.Data["items"]!.AsArray();
        await MarkSalesReportedToZatcaAsync(api);

        async Task<Dictionary<string, (decimal D, decimal C)>> Totals(string entry) => (await api.Get($"/journalentries/{entry}")).Data!["lines"]!.AsArray()
            .GroupBy(l => l!["accountCode"].S()).ToDictionary(g => g.Key, g => (g.Sum(l => l!["debit"].D()), g.Sum(l => l!["credit"].D())));
        var before = await Totals(entryId);
        var stockA = (await api.Get($"/products/{a}")).Data!["currentStock"].D();

        Assert.Equal(400, (await api.Put($"/sales-invoices/{id}/cost-centers", new { costCenterId = east, lines = Array.Empty<object>() })).Status); // السبب إلزامي
        var moved = await api.Put($"/sales-invoices/{id}/cost-centers", new
        {
            costCenterId = east, reason = "تصحيح المركز",
            lines = new[] { new { lineId = lines[0]!["id"].S(), costCenterId = (Guid?)east }, new { lineId = lines[1]!["id"].S(), costCenterId = (Guid?)west } },
        });
        Assert.True(moved.Status == 200, moved.Body?.ToJsonString());
        var newEntry = moved.Data!["journalEntryId"].S();
        Assert.Equal(before, await Totals(newEntry));                                         // الحسابات والمبالغ كما هي
        var centers = (await api.Get($"/journalentries/{newEntry}")).Data!["lines"]!.AsArray()
            .Select(l => l!["costCenterId"]?.GetValue<string>()).Where(c => c != null).Distinct().ToList();
        Assert.DoesNotContain(old.ToString(), centers);
        Assert.Contains(east.ToString(), centers);
        Assert.Contains(west.ToString(), centers);
        Assert.Equal(stockA, (await api.Get($"/products/{a}")).Data!["currentStock"].D());   // لا حركة مخزون
        Assert.Equal(sale.Data["grandTotal"].D(), moved.Data["grandTotal"].D());
        Assert.Equal(sale.Data["invoiceNumber"].S(), moved.Data["invoiceNumber"].S());
    }
}
