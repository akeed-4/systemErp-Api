using System.Text.Json.Nodes;
using ERP.Tests.Infrastructure;

namespace ERP.Tests;

/// <summary>
/// الحذف النهائي للقيد اليدوي مقابل عكسه: العكس قيد جديد مرآة للأصل مرتبط به ولا يتكرر، والحذف إعدام بلا قيد عكسي
/// لا يمس سجل التدقيق، وكلاهما مقيّد بالصلاحية والفترة ونوع القيد.
/// </summary>
[Collection("api")]
public class JournalDeleteReverseTests : TestBase
{
    public JournalDeleteReverseTests(ErpFactory f) : base(f) { }

    private static object Entry(DateTime? date, params (string Code, decimal Debit, decimal Credit, Guid? Center)[] lines)
        => new { date = date ?? DateTime.UtcNow.Date, description = "مصروف صيانة", lines = lines.Select(l => new { accountCode = l.Code, debit = l.Debit, credit = l.Credit, costCenterId = l.Center }).ToArray() };

    private static object Reason(string r = "إدخال خاطئ") => new { reason = r };

    private static async Task<decimal> BalanceAsync(Client api, string code) => (await api.Get($"/accounts/ByCode/{code}")).Data!["balance"].D();

    private static async Task<List<string>> AuditActionsAsync(Client api, string id)
        => (await api.Get($"/auditlogs?entityName=JournalEntry&entityId={id}")).Data!["items"]!.AsArray().Select(x => x!["action"].S()).ToList();

    private static Dictionary<string, (decimal Debit, decimal Credit)> Lines(JsonNode entry)
        => entry["lines"]!.AsArray().ToDictionary(l => l!["accountCode"].S(), l => (l!["debit"].D(), l["credit"].D()));

    // ---------- العكس ----------
    [Fact]
    public async Task Reversal_mirrors_the_original_links_to_it_and_leaves_it_unchanged()
    {
        var api = await NewTenantAsync();
        var created = await api.Post("/costcenters", new { code = "MNT", nameAr = "الصيانة", nameEn = "MNT", isActive = true });
        Assert.Equal(201, created.Status);
        Guid? center = created.Data!["id"].G();
        var original = await api.Post("/journalentries", Entry(null, ("521", 100m, 0m, center), ("1111", 0m, 100m, null)));
        Assert.Equal(200, original.Status);
        var id = original.Data!["id"].S();
        var before = Lines(original.Data);

        var reversal = await api.Post($"/journalentries/{id}/reverse", Reason());
        Assert.Equal(200, reversal.Status);
        var rev = reversal.Data!;

        // قيد جديد برقم مستقل، مدينه دائن الأصل والعكس، بالمراكز نفسها، ومتوازن
        Assert.NotEqual(original.Data["entryNumber"].S(), rev["entryNumber"].S());
        Assert.Equal((0m, 100m), Lines(rev)["521"]);
        Assert.Equal((100m, 0m), Lines(rev)["1111"]);
        Assert.Equal(center.ToString(), rev["lines"]!.AsArray().Single(l => l!["accountCode"].S() == "521")!["costCenterId"].S());
        Assert.Equal(rev["totalDebit"].D(), rev["totalCredit"].D());
        Assert.Contains(original.Data["entryNumber"].S(), rev["description"].S());
        Assert.Contains("إدخال خاطئ", rev["description"].S());

        // مرتبط بالأصل، ونوعه «عكسي»
        Assert.Equal(id, rev["sourceReferenceId"].S());
        Assert.Equal("reversal", rev["entryKind"].S());

        // الأصل كما هو تماماً عدا حالته التوضيحية
        var after = (await api.Get($"/journalentries/{id}")).Data!;
        Assert.Equal("reversed", after["status"].S());
        Assert.Equal(before, Lines(after));
        Assert.Equal(original.Data["totalDebit"].D(), after["totalDebit"].D());
        Assert.Equal(rev["entryNumber"].S(), after["reversedByEntryNumber"].S());
        Assert.Equal("manual", after["entryKind"].S());

        Assert.Equal(0m, await BalanceAsync(api, "521"));
        Assert.Contains("JOURNAL_ENTRY_REVERSED", await AuditActionsAsync(api, id));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    [Fact]
    public async Task Reversal_needs_a_reason_and_is_never_repeated_or_reversed_again()
    {
        var api = await NewTenantAsync();
        var id = (await api.Post("/journalentries", Entry(null, ("521", 50m, 0m, null), ("1111", 0m, 50m, null)))).Data!["id"].S();

        Assert.Equal(400, (await api.Post($"/journalentries/{id}/reverse")).Status);
        Assert.Equal(400, (await api.Post($"/journalentries/{id}/reverse", Reason("   "))).Status);
        Assert.Equal("posted", (await api.Get($"/journalentries/{id}")).Data!["status"].S());

        var reversal = (await api.Post($"/journalentries/{id}/reverse", Reason())).Data!["id"].S();
        Assert.Equal(409, (await api.Post($"/journalentries/{id}/reverse", Reason())).Status);       // لا عكس مكرر
        Assert.Equal(409, (await api.Post($"/journalentries/{reversal}/reverse", Reason())).Status); // ولا عكس للعكسي
        Assert.Equal(409, (await api.Delete($"/journalentries/{reversal}")).Status);                 // ولا حذف للعكسي
        Assert.Equal(0m, await BalanceAsync(api, "521"));
        // حذف الأصل المعكوس: يُمسح مع عكسيه بلا قيد جديد، ويبقى الرصيد صفراً
        var before = (await api.Get("/journalentries?pageSize=50")).Data!["items"]!.AsArray().Count;
        Assert.True((await api.Delete($"/journalentries/{id}")).Status is 200 or 204);
        Assert.Equal(before - 2, (await api.Get("/journalentries?pageSize=50")).Data!["items"]!.AsArray().Count);
        Assert.Equal(0m, await BalanceAsync(api, "521"));
    }

    [Fact]
    public async Task Concurrent_reversal_requests_create_a_single_reversing_entry()
    {
        var api = await NewTenantAsync();
        var id = (await api.Post("/journalentries", Entry(null, ("521", 80m, 0m, null), ("1111", 0m, 80m, null)))).Data!["id"].S();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => api.Post($"/journalentries/{id}/reverse", Reason())));
        Assert.Equal(1, results.Count(r => r.Status == 200));
        Assert.All(results.Where(r => r.Status != 200), r => Assert.Equal(409, r.Status));

        var reversals = (await api.Get("/journalentries?pageSize=50")).Data!["items"]!.AsArray()
            .Count(e => e!["sourceType"].S() == "reversal" && e["sourceReferenceId"].S() == id);
        Assert.Equal(1, reversals);
        Assert.Equal(0m, await BalanceAsync(api, "521"));
        Assert.Equal(0m, await BalanceAsync(api, "1111"));
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);
    }

    // ---------- الحذف النهائي ----------
    [Fact]
    public async Task Deleting_a_manual_entry_erases_it_without_a_reversal_restores_balances_and_keeps_the_audit_trail()
    {
        var api = await NewTenantAsync();
        var (customer, account) = await SeedCustomerAsync(api);
        var countBefore = (await api.Get("/journalentries")).Data!["totalCount"].D();
        var entry = await api.Post("/journalentries", Entry(null, (account, 300m, 0m, null), ("1111", 0m, 300m, null)));
        var id = entry.Data!["id"].S();
        Assert.Equal(300m, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());

        Assert.Equal(200, (await api.Delete($"/journalentries/{id}")).Status);

        // مُسح القيد وأسطره، ولا قيد عكسي
        Assert.Equal(404, (await api.Get($"/journalentries/{id}")).Status);
        Assert.Equal(countBefore, (await api.Get("/journalentries")).Data!["totalCount"].D());
        Assert.DoesNotContain((await api.Get("/journalentries?pageSize=50")).Data!["items"]!.AsArray(), e => e!["sourceReferenceId"].S() == id);

        // الأرصدة ورصيد العميل كما قبل القيد، والميزان متوازن
        Assert.Equal(0m, await BalanceAsync(api, account));
        Assert.Equal(0m, await BalanceAsync(api, "1111"));
        Assert.Equal(0m, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
        var (d, c) = Totals(await api.Get("/reports/TrialBalance")); Assert.Equal(d, c);

        // سجل التدقيق يبقى: إنشاء القيد وحذفه مع صورته
        var actions = await AuditActionsAsync(api, id);
        Assert.Contains("JOURNAL_ENTRY_CREATED", actions);
        Assert.Contains("JOURNAL_ENTRY_DELETED", actions);
        var details = (await api.Get($"/auditlogs?entityName=JournalEntry&entityId={id}")).Data!["items"]!.AsArray()
            .Single(x => x!["action"].S() == "JOURNAL_ENTRY_DELETED")!["details"].S();
        Assert.Contains(entry.Data["entryNumber"].S(), details);

        Assert.Equal(404, (await api.Delete($"/journalentries/{id}")).Status);
    }

    [Fact]
    public async Task Automatic_entries_are_neither_deleted_nor_reversed_and_their_document_stays_posted()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api);
        var inv = await api.PostInvoice(new { kind = "sales", invoiceType = "simplified", paymentMethod = "cash",
            items = new[] { new { itemId = product, quantity = 1, unitPrice = 100, vatRate = 15 } } });
        var jeId = inv.Data!["journalEntryId"].S();

        Assert.Equal("document", (await api.Get($"/journalentries/{jeId}")).Data!["entryKind"].S());
        Assert.Equal(409, (await api.Post($"/journalentries/{jeId}/reverse", Reason())).Status);
        Assert.Equal(409, (await api.Delete($"/journalentries/{jeId}")).Status);
        Assert.Equal("posted", (await api.Get($"/journalentries/{jeId}")).Data!["status"].S());
        var invoice = (await api.Get(await api.InvoicePath(inv.Data["id"].S(), ""))).Data!;
        Assert.Equal("posted", invoice["status"].S());
        Assert.Equal(jeId, invoice["journalEntryId"].S());
    }

    [Fact]
    public async Task Locked_period_entries_are_neither_deleted_nor_reversed()
    {
        var api = await NewTenantAsync();
        var date = DateTime.UtcNow.Date.AddDays(-10);
        var id = (await api.Post("/journalentries", Entry(date, ("521", 40m, 0m, null), ("1111", 0m, 40m, null)))).Data!["id"].S();
        Assert.Equal(200, (await api.Put("/fiscalperiods/lock", new { lockedThrough = date.AddDays(2) })).Status);

        Assert.Equal(409, (await api.Post($"/journalentries/{id}/reverse", Reason())).Status);
        Assert.Equal(409, (await api.Delete($"/journalentries/{id}")).Status);
        Assert.Equal("posted", (await api.Get($"/journalentries/{id}")).Data!["status"].S());
        Assert.Equal(40m, await BalanceAsync(api, "521"));
    }

    [Fact]
    public async Task Users_without_permission_can_neither_delete_nor_reverse()
    {
        var owner = await NewTenantAsync();
        var id = (await owner.Post("/journalentries", Entry(null, ("521", 20m, 0m, null), ("1111", 0m, 20m, null)))).Data!["id"].S();
        var email = $"u{Guid.NewGuid():N}@test.com";
        Assert.Equal(200, (await owner.Post("/users", new { name = "rep", email, phone = "1", password = "Passw0rd!", role = "sales_rep" })).Status);
        var rep = await owner.LoginAsAsync(NewHttp(), email, "Passw0rd!");

        Assert.Equal(403, (await rep.Delete($"/journalentries/{id}")).Status);
        Assert.Equal(403, (await rep.Post($"/journalentries/{id}/reverse", Reason())).Status);
        Assert.Equal("posted", (await owner.Get($"/journalentries/{id}")).Data!["status"].S());
        Assert.Equal(20m, await BalanceAsync(owner, "521"));
    }
}
