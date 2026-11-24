using System.Text.Json.Nodes;
using ERP.Core.Models.Shared;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// الرصيد الجاري في كشف الحساب: يُحسب في قاعدة البيانات بترتيب الكشف الموحّد (اليوم ← وقت التسجيل ← رقم القيد ← السطر)
/// فيحتفظ كل صف برصيده الحقيقي مع الفترة والترقيم والبحث والفلترة والفرز، ويطابق الافتتاحي + المدين − الدائن.
/// </summary>
[Collection("api")]
public class AccountStatementRunningBalanceTests : TestBase
{
    public AccountStatementRunningBalanceTests(ErpFactory f) : base(f) { }

    private const string Cash = "1111";    // مدين الطبيعة
    private const string Bank = "1101";    // مدين الطبيعة
    private const string Capital = "31";   // دائن الطبيعة

    private static string Q(params (string Key, string Json)[] parts)
        => string.Join("&", parts.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Json)}"));

    private static async Task<JsonArray> RowsAsync(Client api, string path)
    {
        var r = await api.Get(path);
        Assert.True(r.Status == 200, $"{path} → {r.Status}: {r.Body}");
        return r.Data!.AsArray();
    }

    /// <summary>قيد يدوي مرحّل: مدين <paramref name="debit"/> ودائن <paramref name="credit"/> بالمبلغ نفسه.</summary>
    private static async Task<string> EntryAsync(Client api, string date, string debit, string credit, decimal amount, string description = "قيد")
    {
        var r = await api.Post("/journalentries", new
        {
            date, description, module = "accounting",
            lines = new[] { new { accountCode = debit, debit = amount, credit = 0m }, new { accountCode = credit, debit = 0m, credit = amount } },
        });
        Assert.True(r.Status == 200, r.Body?.ToJsonString());
        return r.Data!["id"].S();
    }

    private static decimal[] Running(JsonArray rows) => rows.Select(e => e!["runningBalance"].D()).ToArray();

    private static string Period(string? from, string? to)
        => string.Join("&", new[] { from == null ? null : $"from={from}", to == null ? null : $"to={to}" }.Where(p => p != null));

    /// <summary>
    /// ثوابت الكشف: كل صف = الصف السابق (أو الافتتاحي) + حركته بطبيعة الحساب، وآخر صف = الختامي = الافتتاحي + المدين − الدائن (لخانتين)،
    /// وكشف الرأس وصفحات السرفر متطابقان. يعيد الصفوف بترتيب الكشف.
    /// </summary>
    private static async Task<JsonArray> VerifiedAsync(Client api, string account, string? from = null, string? to = null)
    {
        var period = Period(from, to);
        var header = (await api.Get($"/reports/AccountStatement/{account}?{period}")).Data!;
        var rows = await RowsAsync(api, $"/reports/AccountStatement/{account}/Entries?{period}");
        var sign = (bool)header["isDebitNature"]! ? 1m : -1m;
        decimal opening = header["openingBalance"].D(), debit = rows.Sum(r => r!["debit"].D()), credit = rows.Sum(r => r!["credit"].D());

        var expected = opening;
        foreach (var row in rows)
        {
            expected += sign * (row!["debit"].D() - row["credit"].D());
            Assert.Equal(Math.Round(expected, 2), Math.Round(row["runningBalance"].D(), 2));
        }
        Assert.Equal(Math.Round(header["periodDebit"].D(), 2), Math.Round(debit, 2));
        Assert.Equal(Math.Round(header["periodCredit"].D(), 2), Math.Round(credit, 2));
        Assert.Equal(Math.Round(opening + sign * (debit - credit), 2), Math.Round(header["closingBalance"].D(), 2));
        if (rows.Count > 0) Assert.Equal(Math.Round(header["closingBalance"].D(), 2), Math.Round(rows[^1]!["runningBalance"].D(), 2));
        Assert.Equal(Running(rows), Running(header["entries"]!.AsArray()));
        // التسلسل متصل بلا فجوات داخل الفترة (يبدأ بعد حركات ما قبلها)
        var sequences = rows.Select(r => (long)r!["sequence"]!).ToArray();
        Assert.All(sequences.Skip(1).Zip(sequences), p => Assert.Equal(p.Second + 1, p.First));
        return rows;
    }

    // 1 + 10: قيد مدين ثم دائن، وحساب مدين الطبيعة وآخر دائن الطبيعة
    [Fact]
    public async Task Debit_then_credit_on_debit_and_credit_nature_accounts()
    {
        var api = await NewTenantAsync();
        await EntryAsync(api, "2026-01-10", Cash, Capital, 500);   // إيداع رأس مال
        await EntryAsync(api, "2026-01-11", Capital, Cash, 200);   // مسحوبات

        Assert.Equal(new[] { 500m, 300m }, Running(await VerifiedAsync(api, Cash)));
        Assert.Equal(new[] { 500m, 300m }, Running(await VerifiedAsync(api, Capital))); // دائن الطبيعة: الدائن يزيده

        // حساب مدين الطبيعة رصيده دائن: يظهر بالسالب
        await EntryAsync(api, "2026-01-12", Capital, Cash, 450);
        var cash = await VerifiedAsync(api, Cash);
        Assert.Equal(new[] { 500m, 300m, -150m }, Running(cash));
    }

    // 2: عدة قيود في اليوم نفسه، ترتيبها ترتيب تسجيلها وثابت بين الطلبات
    [Fact]
    public async Task Same_day_entries_follow_recording_order_deterministically()
    {
        var api = await NewTenantAsync();
        await EntryAsync(api, "2026-02-01", Cash, Capital, 100, "أول");
        await EntryAsync(api, "2026-02-01", Cash, Capital, 200, "ثانٍ");
        await EntryAsync(api, "2026-02-01", Capital, Cash, 50, "ثالث");

        var rows = await VerifiedAsync(api, Cash);
        Assert.Equal(new[] { 100m, 300m, 250m }, Running(rows));
        Assert.Equal(new[] { "أول", "ثانٍ", "ثالث" }, rows.Select(r => (string)r!["description"]!));
        for (var i = 0; i < 3; i++) Assert.Equal(Running(rows), Running(await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries")));

        // الفرز بالتاريخ = اليوم ثم ترتيب التسجيل، تصاعدياً وتنازلياً
        var asc = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("sort", "[{\"selector\":\"date\",\"desc\":false}]")));
        Assert.Equal(new[] { 100m, 300m, 250m }, Running(asc));
        var desc = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("sort", "[{\"selector\":\"date\",\"desc\":true}]")));
        Assert.Equal(new[] { 250m, 300m, 100m }, Running(desc));
    }

    // 3 + 5 (الافتتاحي): رصيد افتتاحي غير صفري من حركات ما قبل الفترة، لا يتكرر
    [Fact]
    public async Task Non_zero_opening_balance_is_counted_once()
    {
        var api = await NewTenantAsync();
        await EntryAsync(api, "2026-03-01", Cash, Capital, 1000);
        await EntryAsync(api, "2026-03-10", Cash, Capital, 200);
        await EntryAsync(api, "2026-03-12", Capital, Cash, 300);
        await EntryAsync(api, "2026-04-01", Cash, Capital, 999); // بعد الفترة

        var rows = await VerifiedAsync(api, Cash, "2026-03-05", "2026-03-31");
        Assert.Equal(new[] { 1200m, 900m }, Running(rows));
        var header = (await api.Get($"/reports/AccountStatement/{Cash}?from=2026-03-05&to=2026-03-31")).Data!;
        Assert.Equal(1000, header["openingBalance"].D());
        Assert.Equal(900, header["closingBalance"].D());
    }

    // 4: الصفحة الثانية والثالثة تحمل الرصيد التراكمي الحقيقي، وتغيير حجم الصفحة لا يغيّره
    [Fact]
    public async Task Second_and_third_pages_keep_the_true_running_balance()
    {
        var api = await NewTenantAsync();
        for (var i = 1; i <= 7; i++) await EntryAsync(api, $"2026-05-{i:00}", Cash, Capital, i * 100);
        var all = Running(await VerifiedAsync(api, Cash));
        Assert.Equal(new[] { 100m, 300m, 600m, 1000m, 1500m, 2100m, 2800m }, all);

        foreach (var size in new[] { 2, 3 })
            for (var skip = 0; skip < 7; skip += size)
            {
                var page = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("skip", $"{skip}"), ("take", $"{size}"), ("requireTotalCount", "true")));
                Assert.Equal(all.Skip(skip).Take(size), Running(page));
            }
    }

    // 5 + 6: البحث عن حركة وسط الكشف، والفلترة والفرز، لا تغيّر رصيد أي صف
    [Fact]
    public async Task Search_filter_and_sort_keep_each_row_balance()
    {
        var api = await NewTenantAsync();
        for (var i = 1; i <= 5; i++) await EntryAsync(api, $"2026-06-{i:00}", Cash, Capital, i * 100, $"حركة {i}");
        await EntryAsync(api, "2026-06-06", Capital, Cash, 250, "سحب");
        var all = await VerifiedAsync(api, Cash);
        var byId = all.ToDictionary(r => r!["id"].S(), r => r!["runningBalance"].D());

        // البحث (contains كما ترسله الشبكة) عن حركة في الوسط
        var found = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("filter", "[\"description\",\"contains\",\"حركة 4\"]")));
        Assert.Equal(1000, Assert.Single(found)!["runningBalance"].D());

        // فلتر يستبعد الحركات الأولى: الرصيد يبقى شاملاً لها
        var big = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("filter", "[\"debit\",\">\",300]")));
        Assert.Equal(new[] { 1000m, 1500m }, Running(big));
        var credits = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("filter", "[\"credit\",\">\",0]")));
        Assert.Equal(1250, Assert.Single(credits)!["runningBalance"].D());

        // نوع المستند + فرز بالمدين تنازلياً + ترقيم
        var sorted = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(
            ("filter", "[\"referenceType\",\"=\",\"manual\"]"), ("sort", "[{\"selector\":\"debit\",\"desc\":true}]"), ("skip", "1"), ("take", "3")));
        Assert.Equal(new[] { 400m, 300m, 200m }, sorted.Select(r => r!["debit"].D()));
        Assert.All(sorted, r => Assert.Equal(byId[r!["id"].S()], r["runningBalance"].D()));

        // التصفية بالتاريخ داخل الفترة
        var dated = await RowsAsync(api, $"/reports/AccountStatement/{Cash}/Entries?" + Q(("filter", "[\"date\",\">=\",\"2026-06-04\"]")));
        Assert.Equal(new[] { 1000m, 1500m, 1250m }, Running(dated));
    }

    // 7: قيد عكسي بتاريخ الأصل، مسجَّل بعده، يُلغي أثره
    [Fact]
    public async Task Reversal_entry_cancels_the_original_in_the_running_balance()
    {
        var api = await NewTenantAsync();
        await EntryAsync(api, "2026-07-01", Cash, Capital, 500);
        var second = await EntryAsync(api, "2026-07-02", Cash, Capital, 300);
        await EntryAsync(api, "2026-07-03", Cash, Capital, 100);
        var reversed = await api.Post($"/journalentries/{second}/reverse", new { reason = "خطأ في المبلغ" });
        Assert.True(reversed.Status == 200, reversed.Body?.ToJsonString());

        // العكس بتاريخ الأصل (2 يوليو) فيأتي بعده في يومه وقبل قيد 3 يوليو
        var rows = await VerifiedAsync(api, Cash);
        Assert.Equal(new[] { 500m, 800m, 500m, 600m }, Running(rows));
        Assert.Equal(600, (await api.Get($"/reports/AccountStatement/{Cash}")).Data!["closingBalance"].D());
    }

    // 8: المسودة خارج الكشف، والقيد المعكوس (الملغى) يبقى مع عكسه
    [Fact]
    public async Task Draft_entries_are_excluded_and_reversed_entries_stay_with_their_reversal()
    {
        var api = await NewTenantAsync();
        await EntryAsync(api, "2026-08-01", Cash, Capital, 500);
        var cancelled = await EntryAsync(api, "2026-08-02", Cash, Capital, 70);
        Assert.Equal(200, (await api.Post($"/journalentries/{cancelled}/reverse", new { reason = "إلغاء" })).Status);
        var draft = await EntryAsync(api, "2026-08-03", Cash, Capital, 999);
        await SetDraftAsync(Guid.Parse(draft));

        var rows = await VerifiedAsync(api, Cash);
        Assert.Equal(new[] { 500m, 570m, 500m }, Running(rows));
        Assert.DoesNotContain(rows, r => r!["entryId"].S() == draft);
        var header = (await api.Get($"/reports/AccountStatement/{Cash}")).Data!;
        Assert.Equal(570, header["periodDebit"].D()); Assert.Equal(70, header["periodCredit"].D());
        Assert.Equal(500, header["closingBalance"].D());
        // ولا يدخل الرصيد الافتتاحي لفترة بعده
        Assert.Equal(500, (await api.Get($"/reports/AccountStatement/{Cash}?from=2026-09-01")).Data!["openingBalance"].D());
    }

    // 9: حساب رصيده صفر
    [Fact]
    public async Task Account_with_zero_balance()
    {
        var api = await NewTenantAsync();
        await EntryAsync(api, "2026-09-01", Bank, Capital, 400);
        await EntryAsync(api, "2026-09-02", Cash, Bank, 400);

        Assert.Equal(new[] { 400m, 0m }, Running(await VerifiedAsync(api, Bank)));
        var later = (await api.Get($"/reports/AccountStatement/{Bank}?from=2026-10-01")).Data!;
        Assert.Equal(0, later["openingBalance"].D()); Assert.Equal(0, later["closingBalance"].D());
        Assert.Empty(await VerifiedAsync(api, Bank, "2026-10-01"));
    }

    private async Task SetDraftAsync(Guid entryId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var entry = await db.Set<ERP.Core.Models.Accounting.JournalEntry>().IgnoreQueryFilters().FirstAsync(e => e.Id == entryId);
        entry.Status = JournalEntryStatus.Draft;
        await db.SaveChangesAsync();
    }
}
