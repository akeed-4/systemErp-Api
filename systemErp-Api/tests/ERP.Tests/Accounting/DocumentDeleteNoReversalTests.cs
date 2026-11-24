using ERP.Core.Models.Accounting;
using ERP.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Tests;

/// <summary>
/// حذف المستند = حذف قيده وأسطره فقط، بلا قيد عكسي ولا بديل. يُقارَن دفتر المنشأة كله (القيود بحالاتها ومجاميعها وأسطرها،
/// وأرصدة الحسابات) قبل إنشاء المستند وبعد حذفه: يجب أن يعود كما كان تماماً، فلا قيد عكسي أُضيف ولا قيد مستند آخر مُسّ.
/// </summary>
[Collection("api")]
public class DocumentDeleteNoReversalTests : TestBase
{
    public DocumentDeleteNoReversalTests(ErpFactory f) : base(f) { }

    private sealed record Ledger(
        Dictionary<Guid, (string? Source, string Status, decimal Debit, decimal Credit, int Lines)> Entries,
        Dictionary<string, decimal> Balances, int LineCount, int Reversals);

    private async Task<Ledger> LedgerAsync(Client api)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ERP.Service.Data.ErpDbContext>();
        var entries = await db.Set<JournalEntry>().IgnoreQueryFilters().AsNoTracking().Where(e => e.TenantId == api.TenantId)
            .Select(e => new { e.Id, e.SourceType, e.Status, e.TotalDebit, e.TotalCredit, Lines = e.Lines.Count }).ToListAsync();
        var balances = await db.Set<Account>().IgnoreQueryFilters().AsNoTracking().Where(a => a.TenantId == api.TenantId)
            .ToDictionaryAsync(a => a.Code, a => a.Balance);
        var lineCount = await db.Set<JournalEntryLine>().IgnoreQueryFilters().CountAsync(l => l.TenantId == api.TenantId);
        return new Ledger(entries.ToDictionary(e => e.Id, e => (e.SourceType, e.Status.ToString(), e.TotalDebit, e.TotalCredit, e.Lines)),
            balances, lineCount, entries.Count(e => e.SourceType == "reversal"));
    }

    /// <summary>المستند أنشأ قيداً واحداً على الأقل، وحذفه أعاد الدفتر كما كان قبله تماماً.</summary>
    private static void AssertRestored(Ledger before, Ledger created, Ledger after)
    {
        var added = created.Entries.Keys.Except(before.Entries.Keys).ToList();
        Assert.NotEmpty(added);                                         // المستند رُحِّل فعلاً بقيد
        Assert.All(added, id => Assert.False(after.Entries.ContainsKey(id))); // قيده حُذف
        Assert.Equal(0, after.Reversals);                               // لا قيد عكسي
        Assert.Equal(before.Entries.Count, after.Entries.Count);        // لا قيد بديل أو إضافي
        foreach (var (id, e) in before.Entries) Assert.Equal(e, after.Entries[id]); // قيود المستندات الأخرى كما هي
        Assert.Equal(before.LineCount, after.LineCount);                // أسطر القيد حُذفت معه
        foreach (var (code, balance) in before.Balances) Assert.Equal(balance, after.Balances[code]); // الأرصدة عادت
    }

    private static async Task AssertBooksBalancedAsync(Client api)
    {
        var (d, c) = Totals(await api.Get("/reports/TrialBalance"));
        Assert.Equal(d, c);
        var stock = (await api.Get("/reports/InventoryReconciliation")).Data!["rows"]!.AsArray().Single(r => r!["key"].S() == "items")!;
        Assert.Equal(0, stock["difference"].D());
    }

    private static object Sale(Guid product, decimal qty, Guid? customer = null) => new
    {
        kind = "sales", invoiceType = "simplified", paymentMethod = customer == null ? "cash" : "credit", partyId = customer,
        items = new[] { new { itemId = product, quantity = qty, unitPrice = 100, vatRate = 15 } },
    };

    [Fact]
    public async Task Deleting_a_posted_sales_invoice_removes_its_entry_without_reversal_and_leaves_other_invoices_untouched()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 20, cost: 40);
        var (customer, _) = await SeedCustomerAsync(api);
        Assert.Equal(201, (await api.Post("/sales-invoices", Sale(product, 2))).Status); // مستند آخر يجب ألا يُمس
        var customerBefore = (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D();
        var before = await LedgerAsync(api);

        var sale = await api.Post("/sales-invoices", Sale(product, 5, customer));
        Assert.Equal(201, sale.Status);
        Assert.Equal("posted", sale.Data!["status"].S());
        var created = await LedgerAsync(api);
        Assert.Equal(13, (await api.Get($"/products/{product}")).Data!["currentStock"].D());

        Assert.Equal(200, (await api.Delete($"/sales-invoices/{sale.Data["id"].S()}")).Status);
        AssertRestored(before, created, await LedgerAsync(api));
        Assert.Equal(18, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(customerBefore, (await api.Get($"/customers/{customer}")).Data!["currentBalance"].D());
        await AssertBooksBalancedAsync(api);
    }

    [Fact]
    public async Task Deleting_a_posted_purchase_invoice_removes_its_entry_without_reversal()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var (supplier, _) = await SeedSupplierAsync(api);
        var before = await LedgerAsync(api);

        var purchase = await api.Post("/purchase-invoices", new
        {
            kind = "purchase", invoiceType = "tax_invoice", paymentMethod = "credit", partyId = supplier,
            items = new[] { new { itemId = product, quantity = 5, unitPrice = 40, vatRate = 15 } },
        });
        Assert.Equal(201, purchase.Status);
        var created = await LedgerAsync(api);

        Assert.Equal(200, (await api.Delete($"/purchase-invoices/{purchase.Data!["id"].S()}")).Status);
        AssertRestored(before, created, await LedgerAsync(api));
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        Assert.Equal(0, (await api.Get($"/suppliers/{supplier}")).Data!["currentBalance"].D());
        await AssertBooksBalancedAsync(api);
    }

    [Fact]
    public async Task Deleting_receipt_and_payment_vouchers_removes_their_entries_without_reversal()
    {
        var api = await NewTenantAsync();
        var (_, customerAccount) = await SeedCustomerAsync(api);
        var (_, supplierAccount) = await SeedSupplierAsync(api);
        var before = await LedgerAsync(api);

        var receipt = await api.Post("/vouchers", new { type = "receipt", amount = 300, partyName = "عميل", partyAccountCode = customerAccount, treasuryAccountCode = "1111", paymentMethod = "cash" });
        Assert.True(receipt.Success, receipt.Body?.ToJsonString());
        var created = await LedgerAsync(api);
        Assert.Equal(200, (await api.Delete($"/vouchers/{receipt.Data!["id"].S()}")).Status);
        AssertRestored(before, created, await LedgerAsync(api));

        var payment = await api.Post("/vouchers", new { type = "payment", amount = 150, partyName = "مورد", partyAccountCode = supplierAccount, treasuryAccountCode = "1111", paymentMethod = "cash" });
        Assert.True(payment.Success, payment.Body?.ToJsonString());
        created = await LedgerAsync(api);
        Assert.Equal(200, (await api.Delete($"/vouchers/{payment.Data!["id"].S()}")).Status);
        AssertRestored(before, created, await LedgerAsync(api));
        await AssertBooksBalancedAsync(api);
    }

    [Fact]
    public async Task Deleting_a_posted_stock_adjustment_removes_its_entry_without_reversal()
    {
        var api = await NewTenantAsync();
        var product = await SeedProductAsync(api, stock: 10, cost: 40);
        var before = await LedgerAsync(api);

        var damaged = await api.Post("/stockmovements/adjust", new { itemId = product, type = "adjustment_out", quantity = 3, unitCost = 0, referenceNumber = "DMG" });
        Assert.Equal(200, damaged.Status);
        Assert.NotNull(damaged.Data!["journalEntryId"]);
        var created = await LedgerAsync(api);

        Assert.Equal(200, (await api.Delete($"/stockmovements/{damaged.Data["id"].S()}")).Status);
        AssertRestored(before, created, await LedgerAsync(api));
        Assert.Equal(10, (await api.Get($"/products/{product}")).Data!["currentStock"].D());
        await AssertBooksBalancedAsync(api);
    }

    [Fact]
    public async Task Deleting_a_manual_journal_entry_removes_it_and_its_lines_without_reversal()
    {
        var api = await NewTenantAsync();
        await SeedProductAsync(api, stock: 5, cost: 40); // قيد مستند قائم يجب ألا يُمس
        var before = await LedgerAsync(api);

        var entry = await api.Post("/journalentries", new
        {
            date = DateTime.UtcNow.Date, description = "مصروف صيانة",
            lines = new[] { new { accountCode = "521", debit = 120m, credit = 0m }, new { accountCode = "1111", debit = 0m, credit = 120m } },
        });
        Assert.Equal(200, entry.Status);
        var created = await LedgerAsync(api);

        Assert.Equal(200, (await api.Delete($"/journalentries/{entry.Data!["id"].S()}")).Status);
        AssertRestored(before, created, await LedgerAsync(api));
        Assert.Equal(404, (await api.Get($"/journalentries/{entry.Data["id"].S()}")).Status);
        await AssertBooksBalancedAsync(api);
    }
}
