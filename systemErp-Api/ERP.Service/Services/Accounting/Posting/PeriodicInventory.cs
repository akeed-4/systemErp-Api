using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// قيود الجرد الدوري: حساب المخزون لا يتحرك داخل الفترة، ويُثبت رصيده بقيدين فقط:
/// بضاعة آخر المدة عند اعتماد جرد دوري شامل (مدين المخزون / دائن بضاعة آخر المدة) وعكسها إلى بضاعة أول المدة في اليوم التالي،
/// وتحويل رصيد المخزون إلى بضاعة أول المدة مرة واحدة عند بدء الدوري بعد المستمر. بعد أي منهما لا تُسجَّل حركة مخزون بتاريخ سابق
/// فلا تفترق القيمة المثبتة عن الكميات.
/// </summary>
internal static class PeriodicInventory
{
    public const string PeriodEndSource = "inventory_period_end";
    public const string PeriodOpeningSource = "inventory_period_opening";
    public const string MethodOpeningSource = "inventory_method_opening";

    public static bool IsPeriodic(this AccountingSettings s, DateTime date) => s.InventoryMethodOn(date) == InventorySystem.Periodic;

    /// <summary>بداية دوري يلي مستمراً: رصيد المخزون قبلها يُنقل إلى بضاعة أول المدة.</summary>
    private static DateTime? PeriodicStartAfterPerpetual(AccountingSettings s)
        => s is { InventoryMethod: InventorySystem.Periodic, PreviousInventoryMethod: InventorySystem.Perpetual, InventoryMethodEffectiveFrom: { } start } ? start : null;

    /// <summary>يرفض حركة مخزون بتاريخ أُثبتت قيمة المخزون حتى نهايته.</summary>
    public static async Task EnsureMovementDateAllowedAsync(ErpDbContext db, DateTime date, CancellationToken ct)
    {
        var posted = db.Set<JournalEntry>().AsNoTracking().Where(e => e.Status == JournalEntryStatus.Posted);
        var periodEnd = await posted.Where(e => e.SourceType == PeriodEndSource).MaxAsync(e => (DateTime?)e.Date, ct);
        var opening = await posted.Where(e => e.SourceType == PeriodOpeningSource || e.SourceType == MethodOpeningSource).MaxAsync(e => (DateTime?)e.Date, ct);
        // بدء الدوري يُقفل ما قبله بأول حركة بعده، ولو لم يكن للمخزون رصيد يُنقل (فلا قيد تحويل)
        if (PeriodicStartAfterPerpetual(await PostingAccounts.SettingsAsync(db, ct)) is { } start
            && (opening == null || opening < start) && await db.Set<StockMovement>().AnyAsync(m => m.Date >= start, ct))
            opening = start;

        var cutoff = new[] { periodEnd, opening?.AddDays(-1) }.Max();
        if (cutoff is { } through && date.Date <= through)
            throw new ConflictException(string.Format(Messages.InventoryValuedThrough, through));
    }

    /// <summary>تحويل رصيد المخزون إلى بضاعة أول المدة مرة واحدة، مع أول نشاط بتاريخ بدء الدوري أو بعده.</summary>
    public static async Task EnsureMethodOpeningAsync(ErpDbContext db, IAccountingPostingService posting, AccountingSettings s, DateTime date, CancellationToken ct)
    {
        if (PeriodicStartAfterPerpetual(s) is not { } start || date.Date < start) return;
        if (await db.Set<JournalEntry>().AnyAsync(e => e.SourceType == MethodOpeningSource && e.Date == start && e.Status == JournalEntryStatus.Posted, ct)) return;

        // رصيد كل حساب مخزون (الإعدادات وحسابات المستودعات) قبل البدء
        var inventoryAccounts = (await PostingAccounts.WarehouseInventoryAccountsAsync(db, ct)).Append(s.Inventory()).Distinct().ToList();
        var balances = await (from l in db.Set<JournalEntryLine>()
                              join e in db.Set<JournalEntry>() on l.JournalEntryId equals e.Id
                              where inventoryAccounts.Contains(l.AccountCode) && e.Date < start
                              group l by l.AccountCode into g
                              select new { Account = g.Key, Balance = g.Sum(l => l.Debit - l.Credit) }).ToDictionaryAsync(b => b.Account, b => b.Balance, ct);
        if (balances.Values.All(b => b == 0)) return;
        var description = $"تحويل رصيد المخزون إلى بضاعة أول المدة مع بدء الجرد الدوري {start:yyyy-MM-dd}";
        await posting.PostAsync(new GenericPostingRequest
        {
            Date = start, Description = description, SourceType = MethodOpeningSource,
            Lines = AgainstInventory(s.OpeningInventory(), balances, inventoryDebit: false, description),
        }, ct);
    }

    /// <summary>
    /// بضاعة آخر المدة بقيمة كل الأصناف (الكمية × التكلفة) بعد تسوية الجرد موزّعة على حسابات مخزون المستودعات بكمياتها،
    /// وعكسها إلى بضاعة أول المدة في اليوم التالي إن بقي دورياً. قيد واحد لكل تاريخ.
    /// </summary>
    public static async Task<PostingResult?> PostPeriodEndAsync(ErpDbContext db, IAccountingPostingService posting, AccountingSettings s,
        InventoryCount count, DateTime date, CancellationToken ct)
    {
        if (await db.Set<JournalEntry>().AnyAsync(e => e.SourceType == PeriodEndSource && e.Date == date && e.Status == JournalEntryStatus.Posted, ct))
            throw new ConflictException(string.Format(Messages.PeriodEndInventoryAlreadyPosted, date));
        var products = await db.Set<Product>().AsNoTracking().ToListAsync(ct);
        var byAccount = await InventoryValuation.ByAccountAsync(db, s, products.Select(p => (p.Id, InventoryValuation.Value(p))), ct);
        if (byAccount.Values.All(v => v == 0)) return null;

        await DefaultAccounts.EnsureAsync(db, ct, byAccount.Keys.ToArray());
        var description = $"إثبات بضاعة آخر المدة بالجرد {count.CountNumber}";
        var entry = await posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = description, SourceType = PeriodEndSource, SourceId = count.Id, SourceNumber = count.CountNumber,
            Lines = AgainstInventory(s.EndingInventory(), byAccount, inventoryDebit: true, description),
        }, ct);

        var next = date.AddDays(1);
        if (s.IsPeriodic(next))
        {
            var openingDescription = $"بضاعة أول المدة من الجرد {count.CountNumber}";
            await posting.PostAsync(new GenericPostingRequest
            {
                Date = next, Description = openingDescription, SourceType = PeriodOpeningSource, SourceId = count.Id, SourceNumber = count.CountNumber,
                Lines = AgainstInventory(s.OpeningInventory(), byAccount, inventoryDebit: false, openingDescription),
            }, ct);
        }
        return entry;
    }

    /// <summary>
    /// حسابات المخزون بمبالغها (مدينة إن <paramref name="inventoryDebit"/>، والسالب بالعكس) مقابل حساب واحد بالمجموع.
    /// </summary>
    private static List<PostingLine> AgainstInventory(string counter, IReadOnlyDictionary<string, decimal> inventory, bool inventoryDebit, string note)
    {
        var lines = inventory.Where(a => a.Value != 0)
            .Select(a => (a.Value > 0) == inventoryDebit ? new PostingLine(a.Key, Math.Abs(a.Value), 0, note) : new PostingLine(a.Key, 0, Math.Abs(a.Value), note))
            .ToList();
        var total = inventory.Values.Sum();
        if (total != 0) lines.Add((total > 0) == inventoryDebit ? new PostingLine(counter, 0, Math.Abs(total), note) : new PostingLine(counter, Math.Abs(total), 0, note));
        return lines;
    }
}
