using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الفترات المالية: تاريخ إقفال الدفاتر، وإقفال السنة المالية بقيد واحد عبر المحرك المحاسبي يصفّر حسابات
/// الإيرادات والمصروفات عن السنة ويرحّل صافيها إلى الأرباح المبقاة، ثم يقفل الدفاتر حتى نهاية السنة.
/// السنة المقفلة تُعرف بقيد إقفالها القائم؛ عكس القيد يعيد فتحها.
/// </summary>
public class FiscalPeriodService : IFiscalPeriodService
{
    private const string ClosingSource = "year_closing";

    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public FiscalPeriodService(ErpDbContext db, IAccountingPostingService posting, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _posting = posting; _tx = tx; _audit = audit;
    }

    public async Task<FiscalPeriodStatusDto> GetAsync(CancellationToken ct = default)
        => ToStatus(await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct), await ClosedYearsAsync(ct));

    public async Task<FiscalPeriodStatusDto> SetLockAsync(SetPeriodLockDto r, CancellationToken ct = default)
    {
        var tenant = await _db.Set<Tenant>().FirstAsync(ct);
        var closed = await ClosedYearsAsync(ct);
        var target = r.LockedThrough?.Date;
        if (target > DateTime.UtcNow.Date) throw new ValidationFailedException(Messages.PeriodLockDateInFuture);
        // السنة المقفلة تبقى مقفلة: فتحها يكون بعكس قيد إقفالها أولاً
        var lastClosedEnd = closed.Count == 0 ? (DateTime?)null : YearRange(tenant, closed.Max()).To;
        if (lastClosedEnd != null && (target == null || target < lastClosedEnd))
            throw new ConflictException(string.Format(Messages.CannotUnlockClosedYear, lastClosedEnd));

        var previous = tenant.BooksLockedThrough;
        tenant.BooksLockedThrough = target;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PERIOD_LOCK_CHANGED", nameof(Tenant), tenant.Id.ToString(),
            $"تغيير تاريخ إقفال الدفاتر: {Show(previous)} ← {Show(target)}", ct);
        return ToStatus(tenant, closed);
    }

    public Task<YearClosingResultDto> CloseYearAsync(CloseYearRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var tenant = await _db.Set<Tenant>().FirstAsync(token);
            if (r.Year is < 2000 or > 2200) throw new ValidationFailedException(Messages.FiscalYearInvalid);
            var (from, to) = YearRange(tenant, r.Year);
            if (to >= DateTime.UtcNow.Date) throw new ValidationFailedException(string.Format(Messages.FiscalYearNotEnded, to));
            if ((await ClosedYearsAsync(token)).Contains(r.Year)) throw new ConflictException(string.Format(Messages.FiscalYearAlreadyClosed, r.Year));

            // صافي حركة كل حساب إيراد/مصروف خلال السنة (من سطور القيود، لا من الأرصدة الجارية)
            var end = to.AddDays(1);
            var movements = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                                   join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                                   where e.Date >= @from && e.Date < end
                                   group l by l.AccountCode into g
                                   select new { Code = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(token);
            var types = await _db.Set<Account>().AsNoTracking()
                .Where(a => a.Type == AccountCategory.Revenue || a.Type == AccountCategory.Expense).ToDictionaryAsync(a => a.Code, a => a.Type, token);

            var lines = new List<PostingLine>();
            decimal revenues = 0, expenses = 0;
            foreach (var m in movements.Where(m => types.ContainsKey(m.Code)).OrderBy(m => m.Code))
            {
                var net = m.Debit - m.Credit; // موجب = رصيد مدين يُقفل بدائن، والعكس
                if (net == 0) continue;
                lines.Add(net > 0 ? new PostingLine(m.Code, 0, net) : new PostingLine(m.Code, -net, 0));
                if (types[m.Code] == AccountCategory.Revenue) revenues -= net; else expenses += net;
            }
            var profit = revenues - expenses;

            var result = new YearClosingResultDto { Year = r.Year, FromDate = from, ToDate = to, TotalRevenues = revenues, TotalExpenses = expenses, NetProfit = profit };
            var lockedBefore = tenant.BooksLockedThrough;
            if (lines.Count > 0)
            {
                await DefaultAccounts.EnsureAsync(_db, token, DefaultAccounts.RetainedEarnings);
                if (profit != 0)
                    lines.Add(profit > 0 ? new PostingLine(DefaultAccounts.RetainedEarnings, 0, profit) : new PostingLine(DefaultAccounts.RetainedEarnings, -profit, 0));

                // قيد الإقفال يؤرَّخ بآخر يوم في السنة: إن كانت السنة مقفلة يدوياً تُفتح لحظة ترحيله ضمن المعاملة نفسها
                if (lockedBefore >= to) { tenant.BooksLockedThrough = to.AddDays(-1); await _db.SaveChangesAsync(token); }
                var journal = await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = to, Description = $"قيد إقفال السنة المالية {r.Year}",
                    SourceType = ClosingSource, SourceNumber = r.Year.ToString(CultureInfo.InvariantCulture), Lines = lines,
                }, token);
                result.JournalEntryId = journal.JournalEntryId;
                result.JournalEntryNumber = journal.EntryNumber;
            }

            tenant.BooksLockedThrough = lockedBefore > to ? lockedBefore : to;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("FISCAL_YEAR_CLOSED", nameof(Tenant), r.Year.ToString(CultureInfo.InvariantCulture),
                $"إقفال السنة المالية {r.Year} ({from:yyyy-MM-dd} – {to:yyyy-MM-dd}): صافي {profit:0.00}، القيد {result.JournalEntryNumber ?? "—"}", token);
            result.BooksLockedThrough = tenant.BooksLockedThrough.Value;
            return result;
        }, ct);

    private async Task<List<int>> ClosedYearsAsync(CancellationToken ct)
        => (await _db.Set<JournalEntry>().AsNoTracking()
                .Where(e => e.SourceType == ClosingSource && e.Status == JournalEntryStatus.Posted).Select(e => e.ReferenceNumber).ToListAsync(ct))
            .Select(n => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : 0).Where(y => y > 0).Distinct().OrderBy(y => y).ToList();

    /// <summary>السنة المالية تُسمّى بسنة نهايتها؛ بدايتها من إعداد المنشأة (MM-dd) ونهايتها قبل بداية التالية بيوم.</summary>
    internal static (DateTime From, DateTime To) YearRange(Tenant tenant, int year)
    {
        if (!DateTime.TryParseExact(tenant.FinancialYearStart, "MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            start = new DateTime(2000, 1, 1);
        var startYear = start is { Month: 1, Day: 1 } ? year : year - 1;
        var from = new DateTime(startYear, start.Month, Math.Min(start.Day, DateTime.DaysInMonth(startYear, start.Month)));
        return (from, from.AddYears(1).AddDays(-1));
    }

    private static FiscalPeriodStatusDto ToStatus(Tenant t, List<int> closed) => new()
    {
        BooksLockedThrough = t.BooksLockedThrough, FinancialYearStart = t.FinancialYearStart, FinancialYearEnd = t.FinancialYearEnd, ClosedYears = closed,
    };

    private static string Show(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
}
