using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// قيود الجرد الدوري: حساب المخزون لا يتحرك بالمشتريات والمبيعات داخل الفترة، ويحمل قيمة آخر جرد (أو الرصيد الافتتاحي) أصلاً في الميزانية.
/// اعتماد جرد دوري شامل بتاريخ الجرد D يرحّل قيداً موحداً واحداً:
/// <list type="number">
/// <item>إلغاء الرصيد الدفتري X لكل حساب مخزون حتى D: مدين بضاعة أول المدة / دائن المخزون.</item>
/// <item>إثبات القيمة الفعلية Y موزّعة على حسابات مخزون المواقع بكمياتها: مدين المخزون / دائن بضاعة آخر المدة.</item>
/// </list>
/// مقابل حساب مخزون الموقع الإضافي بضاعة أول المدة وآخرها الخاصتان به (<see cref="CountersAsync"/>)؛ والرئيسي وحساب الإعدادات وما يتشاركه أكثر
/// من موقع على حسابي الإعدادات.
/// فيبقى رصيد المخزون Y بعد الجرد، وتكلفة المبيعات = أول المدة + المشتريات − المردودات − آخر المدة. X هو الرصيد الدفتري الفعلي فلا احتساب
/// مزدوجاً مع جرد سابق في السنة نفسها، ولا مع قيود النموذج القديم (عكس آخر المدة في اليوم التالي أو تحويل الرصيد عند بدء الدوري) التي صفّرت
/// المخزون: X عندها صفر وأول المدة مُثبت من قبل. بعد القيد لا تُسجَّل حركة مخزون بتاريخ سابق فلا تفترق القيمة المثبتة عن الكميات.
/// </summary>
internal static class PeriodicInventory
{
    public const string PeriodEndSource = "inventory_period_end";
    /// <summary>النموذج القديم: عكس آخر المدة إلى أول المدة في اليوم التالي (لا يُنشأ بعد الآن؛ قيوده القائمة تبقى).</summary>
    public const string PeriodOpeningSource = "inventory_period_opening";
    /// <summary>النموذج القديم: تحويل رصيد المخزون إلى أول المدة عند بدء الدوري بعد المستمر (لا يُنشأ بعد الآن).</summary>
    public const string MethodOpeningSource = "inventory_method_opening";
    /// <summary>إعادة تصنيف قيد قديم من بضاعة أول المدة إلى المخزون (PeriodicLegacyRemediation).</summary>
    public const string LegacyReclassSource = "inventory_legacy_reclass";

    public static bool IsPeriodic(this AccountingSettings s, DateTime date) => s.InventoryMethodOn(date) == InventorySystem.Periodic;

    /// <summary>بداية دوري يلي مستمراً.</summary>
    private static DateTime? PeriodicStartAfterPerpetual(AccountingSettings s)
        => s is { InventoryMethod: InventorySystem.Periodic, PreviousInventoryMethod: InventorySystem.Perpetual, InventoryMethodEffectiveFrom: { } start } ? start : null;

    /// <summary>يرفض حركة مخزون بتاريخ أُثبتت قيمة المخزون حتى نهايته.</summary>
    public static async Task EnsureMovementDateAllowedAsync(ErpDbContext db, DateTime date, CancellationToken ct)
    {
        var posted = db.Set<JournalEntry>().AsNoTracking().Where(e => e.Status == JournalEntryStatus.Posted);
        var periodEnd = await posted.Where(e => e.SourceType == PeriodEndSource).MaxAsync(e => (DateTime?)e.Date, ct);
        var opening = await posted.Where(e => e.SourceType == PeriodOpeningSource || e.SourceType == MethodOpeningSource).MaxAsync(e => (DateTime?)e.Date, ct);
        // بدء الدوري يُقفل ما قبله بأول حركة بعده (حركات الطريقة السابقة لا تُضاف بعد أن بدأ الدوري)
        if (PeriodicStartAfterPerpetual(await PostingAccounts.SettingsAsync(db, ct)) is { } start
            && (opening == null || opening < start) && await db.Set<StockMovement>().AnyAsync(m => m.Date >= start, ct))
            opening = start;

        var cutoff = new[] { periodEnd, opening?.AddDays(-1) }.Max();
        if (cutoff is { } through && date.Date <= through)
            throw new ConflictException(string.Format(Messages.InventoryValuedThrough, through));
    }

    /// <summary>
    /// جرد دوري بتاريخ D يمثل الكميات في D: يُرفض اعتماده إن وُجدت حركة مخزون بعد D أو سُجّلت بعد إرساله (ولو بتاريخ سابق)، أو كان D مستقبلياً.
    /// المراجع (أول خمسة) وعددها في الرسالة؛ لا تجاوز.
    /// </summary>
    public static async Task EnsureCountRepresentsDateAsync(ErpDbContext db, InventoryCount count, DateTime date, CancellationToken ct)
    {
        if (date > SaudiTime.Today) throw new ValidationFailedException(Messages.CountDateInFuture);
        var submitted = count.SubmittedAt;
        var nextDay = date.Date.AddDays(1); // الحركات تحمل وقت المستند: «بعد تاريخ الجرد» من اليوم التالي
        var conflicts = db.Set<StockMovement>().AsNoTracking()
            .Where(m => !(m.SourceType == InventoryCountSource && m.SourceId == count.Id)
                        && (m.Date >= nextDay || (submitted != null && m.CreatedAt > submitted)));
        var total = await conflicts.CountAsync(ct);
        if (total == 0) return;
        var samples = await conflicts.OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).Take(5)
            .Select(m => new { m.ReferenceNumber, m.Date, m.ItemName }).ToListAsync(ct);
        var list = string.Join("، ", samples.Select(m => $"{(string.IsNullOrWhiteSpace(m.ReferenceNumber) ? m.ItemName : m.ReferenceNumber)} ({m.Date:yyyy-MM-dd})"));
        throw new ConflictException(string.Format(Messages.PeriodicCountLaterMovements, count.CountNumber, date, total, list));
    }

    /// <summary>مصدر حركات تسوية الجرد (InventoryCountApprovalService).</summary>
    public const string InventoryCountSource = "inventory_count";

    /// <summary>
    /// القيد الموحد بتاريخ الجرد (انظر الملخص). Y = قيمة كل الأصناف (الكمية × التكلفة) بعد تسوية الجرد موزّعة على حسابات مخزون المواقع.
    /// قيد واحد لكل تاريخ؛ لا قيد إن كان X وY صفراً.
    /// </summary>
    public static async Task<PostingResult?> PostPeriodEndAsync(ErpDbContext db, IAccountingPostingService posting, AccountingSettings s,
        InventoryCount count, DateTime date, CancellationToken ct)
    {
        if (await db.Set<JournalEntry>().AnyAsync(e => e.SourceType == PeriodEndSource && e.Date == date && e.Status == JournalEntryStatus.Posted, ct))
            throw new ConflictException(string.Format(Messages.PeriodEndInventoryAlreadyPosted, date));
        var products = await db.Set<Product>().AsNoTracking().ToListAsync(ct);
        var actual = await InventoryValuation.ByAccountAsync(db, s, products.Select(p => (p.Id, InventoryValuation.Value(p))), ct);
        var book = await BookBalancesAsync(db, s, date, ct);
        if (actual.Values.All(v => v == 0) && book.Values.All(v => v == 0)) return null;

        await DefaultAccounts.EnsureAsync(db, ct, actual.Keys.Concat(book.Keys).Distinct().ToArray());
        var description = $"قيد الجرد الدوري {count.CountNumber}: تحميل المخزون الدفتري على بضاعة أول المدة وإثبات بضاعة آخر المدة";
        var counters = await CountersAsync(db, s, ct);
        var lines = AgainstInventory(a => counters.TryGetValue(a, out var c) ? c.Opening : s.OpeningInventory(), book, inventoryDebit: false,
                "إلغاء رصيد المخزون الدفتري (بضاعة أول المدة)")
            .Concat(AgainstInventory(a => counters.TryGetValue(a, out var c) ? c.Ending : s.EndingInventory(), actual, inventoryDebit: true,
                "إثبات المخزون الفعلي (بضاعة آخر المدة)"))
            .ToList();
        return await posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = description, SourceType = PeriodEndSource, SourceId = count.Id, SourceNumber = count.CountNumber, Lines = lines,
        }, ct);
    }

    /// <summary>
    /// الرصيد الدفتري حتى <paramref name="date"/> لكل حساب مخزون: حساب الإعدادات وحسابات المواقع، وأي حساب أصل ظهر في جانب المخزون
    /// من قيود الجرد الدوري السابقة أو القديمة أو إعادة تصنيفها (فلا يُهمل حساب موقع تغيّر بعد استخدامه). حساب السيارات لا يدخل.
    /// </summary>
    public static async Task<Dictionary<string, decimal>> BookBalancesAsync(ErpDbContext db, AccountingSettings s, DateTime date, CancellationToken ct)
    {
        var sources = new[] { PeriodEndSource, PeriodOpeningSource, MethodOpeningSource, LegacyReclassSource };
        var historic = await (from l in db.Set<JournalEntryLine>().AsNoTracking()
                              join e in db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                              join a in db.Set<Account>().AsNoTracking() on l.AccountCode equals a.Code
                              where e.SourceType != null && sources.Contains(e.SourceType) && a.Type == AccountCategory.Asset
                              select l.AccountCode).Distinct().ToListAsync(ct);
        var accounts = (await PostingAccounts.WarehouseInventoryAccountsAsync(db, ct)).Append(s.Inventory()).Concat(historic).Distinct().ToList();
        var end = date.Date.AddDays(1);
        return await (from l in db.Set<JournalEntryLine>().AsNoTracking()
                      join e in db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                      where accounts.Contains(l.AccountCode) && e.Date < end
                      group l by l.AccountCode into g
                      select new { Account = g.Key, Balance = g.Sum(l => l.Debit - l.Credit) }).ToDictionaryAsync(b => b.Account, b => b.Balance, ct);
    }

    /// <summary>
    /// بضاعة أول المدة وآخرها لحساب مخزون كل موقع إضافي له حساباه ولا يتشاركه مع موقع آخر. ما ليس في القاموس على حسابي الإعدادات.
    /// </summary>
    private static async Task<Dictionary<string, (string Opening, string Ending)>> CountersAsync(ErpDbContext db, AccountingSettings s, CancellationToken ct)
    {
        var sites = await db.Set<Warehouse>().AsNoTracking().Where(w => w.InventoryAccountCode != null)
            .Select(w => new { w.InventoryAccountCode, w.IsDefault, w.OpeningInventoryAccountCode, w.EndingInventoryAccountCode }).ToListAsync(ct);
        return sites.GroupBy(w => w.InventoryAccountCode!).Where(g => g.Count() == 1 && !g.Single().IsDefault).Select(g => g.Single())
            .ToDictionary(w => w.InventoryAccountCode!, w => (w.OpeningInventoryAccountCode ?? s.OpeningInventory(), w.EndingInventoryAccountCode ?? s.EndingInventory()));
    }

    /// <summary>
    /// حسابات المخزون بمبالغها (مدينة إن <paramref name="inventoryDebit"/>، والسالب بالعكس) مقابل حساب كلٍ منها (<paramref name="counterOf"/>) بمجموع ما يقابله.
    /// </summary>
    private static List<PostingLine> AgainstInventory(Func<string, string> counterOf, IReadOnlyDictionary<string, decimal> inventory, bool inventoryDebit, string note)
    {
        var lines = inventory.Where(a => a.Value != 0)
            .Select(a => (a.Value > 0) == inventoryDebit ? new PostingLine(a.Key, Math.Abs(a.Value), 0, note) : new PostingLine(a.Key, 0, Math.Abs(a.Value), note))
            .ToList();
        foreach (var counter in inventory.GroupBy(a => counterOf(a.Key)).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var total = counter.Sum(a => a.Value);
            if (total != 0)
                lines.Add((total > 0) == inventoryDebit ? new PostingLine(counter.Key, 0, Math.Abs(total), note) : new PostingLine(counter.Key, Math.Abs(total), 0, note));
        }
        return lines;
    }
}
