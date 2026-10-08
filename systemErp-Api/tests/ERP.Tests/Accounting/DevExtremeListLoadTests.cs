using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// قوائم الكيانات (load) تقبل خيارات DevExtreme (filter/sort/skip/take/totalSummary) وتنفّذها في قاعدة البيانات،
/// وتُعيد LoadResult بصفوف من نفس DTO القائمة العادية.
/// </summary>
[Collection("api")]
public class DevExtremeListLoadTests : TestBase
{
    public DevExtremeListLoadTests(ErpFactory f) : base(f) { }

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
    public async Task Crud_load_pages_sorts_and_filters_in_the_database()
    {
        var api = await NewTenantAsync();
        foreach (var name in new[] { "أحمد التجارية", "بدر للمقاولات", "أحمد للسيارات", "خالد" }) await SeedCustomerAsync(api, name: name);

        var all = await api.Get("/customers/load" + Q(("requireTotalCount", "true")));
        Assert.Equal(200, all.Status);
        Assert.Null(all.Body!["success"]); // LoadResult مباشرة دون مغلّف ApiResponse
        Assert.Equal(4, all.Body["totalCount"]!.GetValue<int>());
        Assert.NotNull(all.Body["data"]![0]!["accountCode"]); // نفس DTO القائمة العادية

        var page = await api.Get("/customers/load" + Q(("sort", "[{\"selector\":\"nameAr\",\"desc\":false}]"), ("skip", "1"), ("take", "2"), ("requireTotalCount", "true")));
        var names = page.Body!["data"]!.AsArray().Select(r => r!["nameAr"].S()).ToList();
        Assert.Equal(2, names.Count);
        Assert.Equal(4, page.Body["totalCount"]!.GetValue<int>()); // العدد الكلي لا عدد الصفحة
        Assert.Equal(new[] { "أحمد التجارية", "أحمد للسيارات", "بدر للمقاولات", "خالد" }.OrderBy(x => x, StringComparer.Ordinal).Skip(1).Take(2), names.OrderBy(x => x, StringComparer.Ordinal));

        var search = await api.Get("/customers/load" + Q(("filter", "[[\"nameAr\",\"contains\",\"أحمد\"],\"or\",[\"code\",\"contains\",\"أحمد\"]]"), ("requireTotalCount", "true")));
        Assert.Equal(2, search.Body!["totalCount"]!.GetValue<int>());
        Assert.All(search.Body["data"]!.AsArray(), r => Assert.Contains("أحمد", r!["nameAr"].S()));
    }

    [Fact]
    public async Task Invoice_load_combines_domain_filters_with_devextreme_options()
    {
        var api = await SeededAsync();
        var top = await api.Get("/invoices/load?kind=sales&" + Q(("sort", "[{\"selector\":\"grandTotal\",\"desc\":true}]"), ("take", "1"), ("requireTotalCount", "true"),
            ("totalSummary", "[{\"selector\":\"grandTotal\",\"summaryType\":\"sum\"}]")).TrimStart('?'));
        Assert.Equal(200, top.Status);
        Assert.Equal(3, top.Body!["totalCount"]!.GetValue<int>());
        var first = top.Body["data"]!.AsArray().Single()!;
        Assert.Equal(345, first["grandTotal"].D());
        Assert.Equal(345, first["amountDue"].D()); // المتبقي يُحسب لفواتير الصفحة كما في القائمة العادية
        Assert.NotEmpty(first["items"]!.AsArray());
        Assert.Equal(690, top.Body["summary"]![0].D()); // الملخص على كل الفواتير المفلترة لا على الصفحة

        var none = await api.Get("/invoices/load?kind=purchase&" + Q(("requireTotalCount", "true")).TrimStart('?'));
        Assert.Equal(0, none.Body!["totalCount"]!.GetValue<int>());

        var credit = await api.Get("/invoices/load?kind=sales&" + Q(("filter", "[\"partyName\",\"contains\",\"آجل\"]"), ("requireTotalCount", "true")).TrimStart('?'));
        Assert.Equal(1, credit.Body!["totalCount"]!.GetValue<int>());

        var today = DateTime.UtcNow.Date;
        var dated = await api.Get("/invoices/load?kind=sales&" + Q(("filter", $"[[\"issueDate\",\">=\",\"{today.AddDays(-1):yyyy-MM-dd}\"],\"and\",[\"issueDate\",\"<\",\"{today.AddDays(2):yyyy-MM-dd}\"]]"),
            ("requireTotalCount", "true")).TrimStart('?'));
        Assert.Equal(3, dated.Body!["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Journal_and_ledger_rows_load_with_summaries()
    {
        var api = await SeededAsync();
        var entries = await api.Get("/journalentries/load" + Q(("requireTotalCount", "true"), ("take", "2")));
        Assert.Equal(200, entries.Status);
        Assert.True(entries.Body!["totalCount"]!.GetValue<int>() >= 3);
        Assert.Equal(2, entries.Body["data"]!.AsArray().Count);
        Assert.NotEmpty(entries.Body["data"]![0]!["lines"]!.AsArray());

        var ledger = await api.Get("/reports/JournalLedger" + Q(("requireTotalCount", "true"), ("take", "5"),
            ("totalSummary", "[{\"selector\":\"debit\",\"summaryType\":\"sum\"},{\"selector\":\"credit\",\"summaryType\":\"sum\"}]")));
        Assert.Equal(200, ledger.Status);
        Assert.Equal(5, ledger.Body!["data"]!.AsArray().Count);
        Assert.True(ledger.Body["totalCount"]!.GetValue<int>() > 5);
        Assert.Equal(ledger.Body["summary"]![0].D(), ledger.Body["summary"]![1].D()); // المدين = الدائن على كامل الدفتر
        Assert.NotNull(ledger.Body["data"]![0]!["accountCode"]);

        var balances = await api.Get("/reports/AccountBalances" + Q(("filter", "[\"debit\",\">\",0]"), ("sort", "[{\"selector\":\"debit\",\"desc\":true}]"), ("requireTotalCount", "true")));
        Assert.Equal(200, balances.Status);
        var debits = balances.Body!["data"]!.AsArray().Select(r => r!["debit"].D()).ToList();
        Assert.NotEmpty(debits);
        Assert.All(debits, d => Assert.True(d > 0));
        Assert.Equal(debits.OrderByDescending(x => x), debits);
    }

    [Fact]
    public async Task Invalid_options_return_400_and_load_still_requires_authentication()
    {
        var api = await SeededAsync();
        Assert.Equal(400, (await api.Get("/customers/load" + Q(("filter", "[\"noSuchField\",\"=\",1]")))).Status);
        Assert.Equal(400, (await api.Get("/invoices/load?kind=sales&" + Q(("sort", "[{\"selector\":\"noSuchField\"}]")).TrimStart('?'))).Status);
        Assert.Equal(401, (await new Client(NewHttp()).Get("/customers/load")).Status);
        Assert.Equal(401, (await new Client(NewHttp()).Get("/invoices/load?kind=sales")).Status);
    }
}
