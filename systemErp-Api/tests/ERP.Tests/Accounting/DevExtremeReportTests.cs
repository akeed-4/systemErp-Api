using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>التقارير القائمية تقبل خيارات DevExtreme (filter/sort/skip/take/group/summary) وتُعيد LoadResult.</summary>
[Collection("api")]
public class DevExtremeReportTests : TestBase
{
    public DevExtremeReportTests(ErpFactory f) : base(f) { }

    private static string Q(params (string Key, string Json)[] parts)
        => "?" + string.Join("&", parts.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Json)}"));

    private async Task<Client> SeededAsync()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 50);
        var (customer, _) = await SeedCustomerAsync(api);
        foreach (var qty in new[] { 1, 2, 3 })
            await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = qty == 3 ? "credit" : "cash", partyId = qty == 3 ? customer : (Guid?)null,
                items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } } });
        return api;
    }

    [Fact]
    public async Task Response_is_a_raw_LoadResult_not_wrapped_in_ApiResponse()
    {
        var api = await SeededAsync();
        var r = await api.Get("/reports/trial-balance" + Q(("requireTotalCount", "true")));
        Assert.Equal(200, r.Status);
        Assert.Null(r.Body!["success"]);
        Assert.NotNull(r.Body["data"]); Assert.True(r.Body["totalCount"]!.GetValue<int>() > 3);
    }

    [Fact]
    public async Task Filter_sort_and_paging_are_applied()
    {
        var api = await SeededAsync();
        var all = (await api.Get("/reports/trial-balance" + Q(("requireTotalCount", "true")))).Body!;
        var total = all["totalCount"]!.GetValue<int>();

        var filtered = await api.Get("/reports/trial-balance" + Q(("filter", "[\"periodDebit\",\">\",0]"), ("sort", "[{\"selector\":\"periodDebit\",\"desc\":true}]"), ("requireTotalCount", "true")));
        var rows = filtered.Body!["data"]!.AsArray();
        Assert.All(rows, r => Assert.True(r!["periodDebit"].D() > 0));
        Assert.True(filtered.Body["totalCount"]!.GetValue<int>() < total);
        var debits = rows.Select(r => r!["periodDebit"].D()).ToList();
        Assert.Equal(debits.OrderByDescending(x => x), debits);

        var page = await api.Get("/reports/trial-balance" + Q(("sort", "[{\"selector\":\"accountCode\"}]"), ("skip", "1"), ("take", "2"), ("requireTotalCount", "true")));
        Assert.Equal(2, page.Body!["data"]!.AsArray().Count); Assert.Equal(total, page.Body["totalCount"]!.GetValue<int>());
        var codes = all["data"]!.AsArray().Select(r => r!["accountCode"].S()).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(codes[1], page.Body["data"]![0]!["accountCode"].S());

        var search = await api.Get("/reports/trial-balance" + Q(("filter", "[\"accountCode\",\"startswith\",\"112\"]")));
        Assert.All(search.Body!["data"]!.AsArray(), r => Assert.StartsWith("112", r!["accountCode"].S()));
    }

    [Fact]
    public async Task Total_summary_and_grouping_with_group_summary()
    {
        var api = await SeededAsync();
        var sum = await api.Get("/reports/trial-balance" + Q(("totalSummary", "[{\"selector\":\"periodDebit\",\"summaryType\":\"sum\"},{\"selector\":\"periodCredit\",\"summaryType\":\"sum\"}]"), ("take", "1")));
        var summary = sum.Body!["summary"]!.AsArray();
        Assert.Equal(summary[0].D(), summary[1].D()); // المدين = الدائن حتى عبر ملخص DevExtreme

        var grouped = await api.Get("/reports/trial-balance" + Q(("group", "[{\"selector\":\"accountNameAr\",\"isExpanded\":false}]"),
            ("groupSummary", "[{\"selector\":\"periodDebit\",\"summaryType\":\"sum\"}]"), ("requireGroupCount", "true")));
        var groups = grouped.Body!["data"]!.AsArray();
        Assert.NotEmpty(groups);
        Assert.NotNull(groups[0]!["key"]); Assert.NotNull(groups[0]!["summary"]);
        Assert.True(grouped.Body["groupCount"]!.GetValue<int>() >= groups.Count);
    }

    [Fact]
    public async Task Domain_filters_and_devextreme_options_combine_on_inventory_reports()
    {
        var api = await SeededAsync();
        var product = (await api.Get("/products")).Data!["items"]![0]!["id"].S();
        var ledger = await api.Get($"/reports/item-ledger?itemId={product}&" + Q(("filter", "[\"quantityOut\",\">\",0]"), ("requireTotalCount", "true")).TrimStart('?'));
        Assert.Equal(3, ledger.Body!["totalCount"]!.GetValue<int>()); // ثلاث حركات صرف
        Assert.All(ledger.Body["data"]!.AsArray(), r => Assert.True(r!["quantityOut"].D() > 0));

        var audit = await api.Get("/reports/inventory-audit" + Q(("filter", "[\"systemQuantity\",\">\",0]"), ("select", "[\"sku\",\"systemQuantity\"]")));
        Assert.Single(audit.Body!["data"]!.AsArray());
        var trade = await api.Get("/reports/trade-commercial" + Q(("sort", "[{\"selector\":\"grandTotal\",\"desc\":true}]"), ("take", "1")));
        Assert.Equal(345, trade.Body!["data"]![0]!["grandTotal"].D());
        var statement = await api.Get("/reports/account-statement/1111/entries" + Q(("requireTotalCount", "true")));
        Assert.Equal(200, statement.Status);
    }

    [Fact]
    public async Task Car_reports_accept_devextreme_options()
    {
        var api = await NewTenantAsync();
        foreach (var path in new[] { "sales-performance", "vin-inventory", "zatca-margin-tax", "procurement-tracking", "profit-loss", "installments-receivable", "suppliers-procurement", "daily-monthly-sales" })
        {
            var r = await api.Get($"/reports/car/{path}" + Q(("requireTotalCount", "true"), ("skip", "0"), ("take", "10")));
            Assert.True(r.Status == 200, path);
            Assert.NotNull(r.Body!["data"]); Assert.Equal(0, r.Body["totalCount"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task Invalid_options_return_400_and_reports_still_require_permission()
    {
        var api = await SeededAsync();
        var bad = await api.Get("/reports/trial-balance" + Q(("filter", "[\"noSuchField\",\"=\",1]")));
        Assert.Equal(400, bad.Status);
        Assert.Equal(401, (await new Client(NewHttp()).Get("/reports/trial-balance")).Status);
    }
}
