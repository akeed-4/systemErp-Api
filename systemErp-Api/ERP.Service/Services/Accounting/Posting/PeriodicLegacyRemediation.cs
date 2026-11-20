using System.Security.Cryptography;
using System.Text;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// معالجة قيود الجرد الدوري القديمة لمنشأة واحدة (بطلب صريح بعد معاينة): النموذج السابق نقل قيمة أصل المخزون إلى بضاعة أول المدة (مصروف)
/// بعكس آخر المدة في اليوم التالي، أو بتحويل الرصيد عند بدء الدوري، أو بترحيل الأرصدة الافتتاحية عليها؛ فظهر المخزون صفراً في الميزانية.
/// <para>
/// لكل قيد قديم في الدورة الحالية (بعد آخر قيد آخر مدة) قيد إعادة تصنيف جديد بتاريخه: مدين حسابات المخزون نفسها التي خُصمت فيه (أو حساب
/// مستودع حركة التسوية) / دائن بضاعة أول المدة. لا يُعدَّل القيد القديم ولا يُعكس (حالته يعتمد عليها قفل تواريخ الحركات). الجرد التالي يحمّل
/// الرصيد إلى أول المدة بقيده الموحد، فتكلفة مبيعات الدورة كما كانت ستكون بلا معالجة، والميزانية صحيحة من تاريخ القيد القديم.
/// </para>
/// قيد في فترة مقفلة يدوياً لا يُعالج إلا بفتحها أو باختيار صريح لأول يوم مفتوح في سنته المالية؛ وقيد في سنة مقفلة لا يُعالج آلياً
/// (صافيه أُقفل في الأرباح المبقاة). التشغيل المتكرر لا يكرر: القيد المعالَج له قيد إعادة تصنيف فعّال؛ وعكس هذا القيد يعيد المعالجة متاحة.
/// </summary>
internal static class PeriodicLegacyRemediation
{
    private const string StockAdjustmentSource = "stock_adjustment";

    public static async Task<PeriodicLegacyPreviewDto> PreviewAsync(ErpDbContext db, CancellationToken ct)
    {
        var s = await PostingAccounts.SettingsAsync(db, ct);
        var preview = new PeriodicLegacyPreviewDto { Applicable = s.InventoryMethod == InventorySystem.Periodic };
        if (!preview.Applicable || s.OpeningInventoryAccountCode is not { } opening) return Finish(preview);

        var tenant = await db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        var entries = db.Set<JournalEntry>().AsNoTracking();
        var cycleStart = await entries.Where(e => e.Status == JournalEntryStatus.Posted && e.SourceType == PeriodicInventory.PeriodEndSource)
            .MaxAsync(e => (DateTime?)e.Date, ct);
        preview.CycleStart = cycleStart;
        preview.FirstOpenDate = tenant.BooksLockedThrough?.AddDays(1);
        var closedEnd = await FiscalPeriodService.LastClosedYearEndAsync(db, tenant, ct);

        // القيود تحمل وقت المستند: الدورة الحالية من اليوم التالي لآخر جرد
        DateTime? cycleFrom = cycleStart?.Date.AddDays(1);
        var done = await entries.Where(e => e.Status == JournalEntryStatus.Posted && e.SourceType == PeriodicInventory.LegacyReclassSource && e.SourceReferenceId != null)
            .Select(e => e.SourceReferenceId!.Value).ToListAsync(ct);
        var legacy = await entries.Include(e => e.Lines)
            .Where(e => e.Status == JournalEntryStatus.Posted && (cycleFrom == null || e.Date >= cycleFrom)
                        && (e.SourceType == PeriodicInventory.PeriodOpeningSource || e.SourceType == PeriodicInventory.MethodOpeningSource
                            || (e.SourceType == StockAdjustmentSource && e.Lines.Any(l => l.AccountCode == opening)))
                        && !done.Contains(e.Id))
            .OrderBy(e => e.Date).ThenBy(e => e.EntryNumber).ToListAsync(ct);
        if (legacy.Count == 0) return Finish(preview);

        var assets = await db.Set<Account>().AsNoTracking().Where(a => a.Type == AccountCategory.Asset).Select(a => a.Code).ToListAsync(ct);
        var assetSet = assets.ToHashSet();
        foreach (var e in legacy)
        {
            var lines = e.SourceType == StockAdjustmentSource
                ? await AdjustmentLinesAsync(db, s, e, opening, ct)
                : e.Lines.Select(l => new PeriodicLegacyLineDto { AccountCode = l.AccountCode, Debit = l.Credit, Credit = l.Debit }).ToList(); // مرآة القيد
            // المبلغ المنقول إلى أول المدة = ما يُعاد إلى حسابات المخزون (أصول)
            var amount = lines.Where(l => assetSet.Contains(l.AccountCode)).Sum(l => l.Debit - l.Credit);
            if (amount == 0) continue;

            var item = new PeriodicLegacyEntryDto
            {
                JournalEntryId = e.Id, EntryNumber = e.EntryNumber, Date = e.Date, Amount = amount, Lines = lines,
                Kind = e.SourceType switch
                {
                    PeriodicInventory.PeriodOpeningSource => "period_opening",
                    PeriodicInventory.MethodOpeningSource => "method_opening",
                    _ => "opening_balance",
                },
            };
            if (closedEnd is { } closed && e.Date.Date <= closed) item.Status = PeriodicLegacyStatus.ClosedYear;
            else if (tenant.BooksLockedThrough is { } locked && e.Date.Date <= locked)
            {
                item.Status = PeriodicLegacyStatus.LockedPeriod;
                var year = FiscalPeriodService.YearOf(tenant, e.Date);
                if (locked.AddDays(1) <= year.To) item.FallbackDate = locked.AddDays(1);
            }
            else item.Status = PeriodicLegacyStatus.Ready;
            preview.Entries.Add(item);
        }
        return Finish(preview);
    }

    /// <summary>تسوية قديمة على بضاعة أول المدة: يُنقل سطرها إلى حساب مخزون مستودع الحركة (كما تُرحَّل التسوية الآن).</summary>
    private static async Task<List<PeriodicLegacyLineDto>> AdjustmentLinesAsync(ErpDbContext db, AccountingSettings s, JournalEntry e, string opening,
        CancellationToken ct)
    {
        var amount = e.Lines.Where(l => l.AccountCode == opening).Sum(l => l.Debit - l.Credit);
        var warehouseId = e.SourceReferenceId is { } movementId
            ? await db.Set<StockMovement>().AsNoTracking().Where(m => m.Id == movementId).Select(m => m.WarehouseId).FirstOrDefaultAsync(ct)
            : null;
        var inventory = await s.InventoryAsync(db, warehouseId, ct);
        var value = Math.Abs(amount);
        return amount > 0
            ? new() { new() { AccountCode = inventory, Debit = value }, new() { AccountCode = opening, Credit = value } }
            : new() { new() { AccountCode = opening, Debit = value }, new() { AccountCode = inventory, Credit = value } };
    }

    private static PeriodicLegacyPreviewDto Finish(PeriodicLegacyPreviewDto p)
    {
        p.ReadyAmount = p.Entries.Where(e => e.Status == PeriodicLegacyStatus.Ready).Sum(e => e.Amount);
        p.LockedAmount = p.Entries.Where(e => e.Status == PeriodicLegacyStatus.LockedPeriod).Sum(e => e.Amount);
        p.ClosedYearAmount = p.Entries.Where(e => e.Status == PeriodicLegacyStatus.ClosedYear).Sum(e => e.Amount);
        var raw = string.Join("|", p.Entries.Select(e => $"{e.JournalEntryId}:{e.Amount:0.####}:{e.Status}:{e.FallbackDate:yyyyMMdd}"));
        p.Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return p;
    }

    /// <summary>
    /// ينفّذ المعالجة في معاملة المستدعي: البصمة تطابق المعاينة الحالية، وكل قيد جاهز (أو مقفل باختيار أول يوم مفتوح) يُعاد تصنيفه مرة واحدة.
    /// </summary>
    public static async Task<PeriodicLegacyResultDto> ApplyAsync(ErpDbContext db, IAccountingPostingService posting, ApplyPeriodicLegacyDto r,
        CancellationToken ct)
    {
        var preview = await PreviewAsync(db, ct);
        if (!preview.Applicable) throw new ValidationFailedException(Messages.PeriodicLegacyNotApplicable);
        if (!string.Equals(r.Fingerprint, preview.Fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException(Messages.PeriodicLegacyChanged);

        var result = new PeriodicLegacyResultDto();
        foreach (var e in preview.Entries)
        {
            var date = e.Status == PeriodicLegacyStatus.Ready ? e.Date
                : e.Status == PeriodicLegacyStatus.LockedPeriod && r.UseFirstOpenDateForLocked ? e.FallbackDate : null;
            if (date is not { } on) continue;
            if (await db.Set<JournalEntry>().AnyAsync(x => x.SourceType == PeriodicInventory.LegacyReclassSource && x.SourceReferenceId == e.JournalEntryId
                                                         && x.Status == JournalEntryStatus.Posted, ct)) continue;

            var note = $"إعادة تصنيف قيد الجرد الدوري القديم {e.EntryNumber} من بضاعة أول المدة إلى المخزون";
            var entry = await posting.PostAsync(new GenericPostingRequest
            {
                Date = on, Description = note, SourceType = PeriodicInventory.LegacyReclassSource, SourceId = e.JournalEntryId, SourceNumber = e.EntryNumber,
                Lines = e.Lines.Select(l => new PostingLine(l.AccountCode, l.Debit, l.Credit, note)).ToList(),
            }, ct);
            result.Posted.Add(new PeriodicLegacyPostedDto
            {
                LegacyEntryNumber = e.EntryNumber, JournalEntryId = entry.JournalEntryId, EntryNumber = entry.EntryNumber, Date = on, Amount = e.Amount,
            });
        }
        if (result.Posted.Count == 0) throw new ValidationFailedException(Messages.PeriodicLegacyNothingToApply);
        result.Remaining = await PreviewAsync(db, ct);
        return result;
    }

    /// <summary>تنبيهات الميزانية: مبالغ أصل مخزون ما زالت في بضاعة أول المدة بقيود قديمة (القابلة للمعالجة، وما في سنوات مقفلة).</summary>
    public static async Task<List<string>> WarningsAsync(ErpDbContext db, CancellationToken ct)
    {
        var warnings = new List<string>();
        if ((await PostingAccounts.SettingsAsync(db, ct)).InventoryMethod != InventorySystem.Periodic) return warnings;
        var p = await PreviewAsync(db, ct);
        var pending = p.ReadyAmount + p.LockedAmount;
        if (pending != 0) warnings.Add(string.Format(Messages.PeriodicLegacyBalanceWarning, pending));
        if (p.ClosedYearAmount != 0) warnings.Add(string.Format(Messages.PeriodicLegacyClosedYearWarning, p.ClosedYearAmount));
        return warnings;
    }
}
