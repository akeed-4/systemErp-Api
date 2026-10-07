using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>القوائم المالية الرسمية: الدخل والمركز المالي والتدفقات النقدية من سطور القيود بفترات ومقارنة.</summary>
[Collection("api")]
public class FinancialStatementTests : TestBase
{
    public FinancialStatementTests(ErpFactory f) : base(f) { }

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    [Fact]
    public async Task Statements_are_built_from_posted_entries_for_the_period_and_tie_together()
    {
        var api = await NewTenantAsync();
        var item = await SeedProductAsync(api);
        object Entry(string debit, string credit, decimal amount) => new { date = DateTime.UtcNow, description = "x",
            lines = new[] { new { accountCode = debit, debit = amount, credit = 0m }, new { accountCode = credit, debit = 0m, credit = amount } } };
        Assert.Equal(200, (await api.Post("/journalentries", Entry("1111", "31", 1000))).Status);  // رأس مال نقداً: تمويلي
        Assert.Equal(200, (await api.Post("/journalentries", Entry("121", "1111", 300))).Status);  // شراء أصل ثابت: استثماري
        Assert.Equal(201, (await api.Post("/invoices", new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash", status = "posted",
            items = new[] { new { itemId = item, quantity = 3, unitPrice = 50, vatRate = 15 } } })).Status);

        var from = DateTime.UtcNow.Date.AddDays(-2).ToString("yyyy-MM-dd"); var to = DateTime.UtcNow.Date.AddDays(2).ToString("yyyy-MM-dd");
        var lastYearFrom = DateTime.UtcNow.Date.AddYears(-1).AddDays(-2).ToString("yyyy-MM-dd"); var lastYearTo = DateTime.UtcNow.Date.AddYears(-1).AddDays(2).ToString("yyyy-MM-dd");

        var income = (await api.Get($"/financialstatements/IncomeStatement?from={from}&to={to}&compareFrom={lastYearFrom}&compareTo={lastYearTo}")).Data!;
        Assert.Equal(150, income["revenue"]!["total"].D());
        Assert.True(income["costOfSales"]!["total"].D() > 0);
        Assert.Equal(income["revenue"]!["total"].D() - income["costOfSales"]!["total"].D(), income["grossProfit"]!["total"].D());
        Assert.Equal(income["grossProfit"]!["total"].D() - income["operatingExpenses"]!["total"].D(), income["netProfit"]!["total"].D());
        Assert.Equal(0, income["revenue"]!["comparative"].D()); // لا حركة في فترة المقارنة

        var position = (await api.Get($"/financialstatements/FinancialPosition?asOf={to}&compareAsOf={lastYearTo}")).Data!;
        Assert.True(position["isBalanced"]!.GetValue<bool>());
        Assert.Equal(300, position["nonCurrentAssets"]!["total"].D());
        Assert.Equal(income["netProfit"]!["total"].D(), position["unclosedProfit"]!["total"].D());
        Assert.Equal(0, position["totalAssets"]!["comparative"].D());

        var cash = (await api.Get($"/financialstatements/CashFlow?from={from}&to={to}")).Data!;
        Assert.Equal(-300, cash["investing"]!["total"].D());
        Assert.Equal(1000, cash["financing"]!["total"].D());
        Assert.Equal(cash["openingCash"]!["total"].D() + cash["netChange"]!["total"].D(), cash["closingCash"]!["total"].D());
        Assert.Equal(await BalanceAsync(api, "1111") + await BalanceAsync(api, "1112") + await BalanceAsync(api, "1113"), cash["closingCash"]!["total"].D());

        Assert.Equal(400, (await api.Get($"/financialstatements/IncomeStatement?from={to}&to={from}")).Status);
    }
}
