using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>التسوية البنكية: لا تُعتمد إلا إذا ساوى رصيد الحركات المطابَقة رصيد الكشف، وغير المطابَق يبقى معلّقاً.</summary>
[Collection("api")]
public class BankReconciliationTests : TestBase
{
    public BankReconciliationTests(ErpFactory f) : base(f) { }

    [Fact]
    public async Task Reconciliation_completes_only_when_matched_lines_equal_the_statement_balance()
    {
        var api = await NewTenantAsync();
        const string Bank = "1112";
        async Task<string> EntryAsync(string debit, string credit, decimal amount)
        {
            var r = await api.Post("/journalentries", new { date = DateTime.UtcNow.AddDays(-1), description = "x",
                lines = new[] { new { accountCode = debit, debit = amount, credit = 0m }, new { accountCode = credit, debit = 0m, credit = amount } } });
            Assert.Equal(200, r.Status);
            return r.Data!["id"].S();
        }
        var deposit = await EntryAsync(Bank, "31", 1000);
        await EntryAsync("521", Bank, 200);   // صُرف من البنك
        await EntryAsync("521", Bank, 150);   // شيك لم يُصرف بعد

        var date = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var sheet = (await api.Get($"/bankreconciliations/Worksheet?accountCode={Bank}&statementDate={date}")).Data!;
        Assert.Equal(650, sheet["bookBalance"].D());
        var lines = sheet["unreconciledLines"]!.AsArray();
        Assert.Equal(3, lines.Count);
        var cleared = lines.Where(l => l!["debit"].D() == 1000 || l!["credit"].D() == 200).Select(l => l!["lineId"].S()).ToArray();

        // رصيد الكشف 800 = 1000 − 200؛ برصيد آخر تُرفض
        Assert.Equal(409, (await api.Post("/bankreconciliations", new { accountCode = Bank, statementDate = date, statementBalance = 790, lineIds = cleared })).Status);
        Assert.Equal(400, (await api.Post("/bankreconciliations", new { accountCode = "211", statementDate = date, statementBalance = 0, lineIds = Array.Empty<string>() })).Status);
        var done = await api.Post("/bankreconciliations", new { accountCode = Bank, statementDate = date, statementBalance = 800, lineIds = cleared });
        Assert.Equal(200, done.Status);
        Assert.Equal(150, done.Data!["outstandingPayments"].D());
        Assert.Equal(650, done.Data["bookBalance"].D());

        // المطابَق لا يظهر ثانية، وقيده لا يُحذف
        var after = (await api.Get($"/bankreconciliations/Worksheet?accountCode={Bank}&statementDate={date}")).Data!;
        Assert.Single(after["unreconciledLines"]!.AsArray());
        Assert.Equal(800, after["previouslyReconciledBalance"].D());
        Assert.Equal(409, (await api.Delete($"/journalentries/{deposit}")).Status);

        // إلغاء التسوية يعيد الحركات
        Assert.Equal(200, (await api.Delete($"/bankreconciliations/{done.Data["id"].S()}")).Status);
        Assert.Equal(3, (await api.Get($"/bankreconciliations/Worksheet?accountCode={Bank}&statementDate={date}")).Data!["unreconciledLines"]!.AsArray().Count);
    }
}
