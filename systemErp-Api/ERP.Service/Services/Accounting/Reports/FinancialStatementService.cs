using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// القوائم المالية الرسمية: الدخل، المركز المالي، التدفقات النقدية. كلها من سطور القيود المرحّلة في فترتها
/// (لا من الأرصدة الجارية)، ولكل قائمة عمود مقارنة اختياري بفترة أخرى.
/// </summary>
public class FinancialStatementService : IFinancialStatementService
{
    private const string YearClosingSource = "year_closing";
    private const string CostOfSalesGroup = "51";
    private const string NonCurrentAssetsGroup = "12";
    private const string LongTermLiabilitiesGroup = "22";

    private readonly ErpDbContext _db;
    public FinancialStatementService(ErpDbContext db) => _db = db;

    private sealed record Movement(string Code, decimal Net);

    /// <summary>صافي (مدين − دائن) لكل حساب في [from, to] شاملاً اليومين؛ from الفارغ = منذ البداية.</summary>
    private async Task<List<Movement>> NetByAccountAsync(DateTime? from, DateTime to, bool excludeYearClosing, CancellationToken ct)
    {
        var end = to.Date.AddDays(1);
        var start = @from;
        var q = from l in _db.Set<JournalEntryLine>().AsNoTracking()
                join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                where e.Date < end && (start == null || e.Date >= start) && (!excludeYearClosing || e.SourceType != YearClosingSource)
                group l by l.AccountCode into g
                select new { Code = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) };
        return (await q.ToListAsync(ct)).Select(m => new Movement(m.Code, m.Debit - m.Credit)).ToList();
    }

    private Task<Dictionary<string, Account>> AccountsAsync(CancellationToken ct)
        => _db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Code, ct);

    /// <summary>يبني قسماً من مبالغ الفترة ومبالغ المقارنة لحسابات يختارها <paramref name="include"/>، بإشارة القسم.</summary>
    private static StatementSectionDto Section(Dictionary<string, Account> accounts, IEnumerable<Movement> current, IEnumerable<Movement>? comparative,
        Func<Account, bool> include, int sign)
    {
        var lines = new Dictionary<string, StatementLineDto>();
        void Add(IEnumerable<Movement> movements, Action<StatementLineDto, decimal> assign)
        {
            foreach (var m in movements)
            {
                if (m.Net == 0 || !accounts.TryGetValue(m.Code, out var account) || !include(account)) continue;
                if (!lines.TryGetValue(m.Code, out var line))
                    lines[m.Code] = line = new StatementLineDto { AccountCode = account.Code, NameAr = account.NameAr, NameEn = account.NameEn };
                assign(line, DocumentPricing.Round(sign * m.Net));
            }
        }
        Add(current, (line, amount) => line.Amount += amount);
        if (comparative != null) Add(comparative, (line, amount) => line.Comparative += amount);

        var ordered = lines.Values.Where(l => l.Amount != 0 || l.Comparative != 0).OrderBy(l => l.AccountCode, StringComparer.Ordinal).ToList();
        return new StatementSectionDto { Lines = ordered, Total = ordered.Sum(l => l.Amount), Comparative = ordered.Sum(l => l.Comparative) };
    }

    private static StatementSectionDto Total(params (StatementSectionDto Section, int Sign)[] parts)
        => new() { Total = parts.Sum(p => p.Sign * p.Section.Total), Comparative = parts.Sum(p => p.Sign * p.Section.Comparative) };

    private static void EnsurePeriod(DateTime from, DateTime to)
    {
        if (to.Date < from.Date) throw new ValidationFailedException(Messages.PeriodEndBeforeStart);
    }

    // ---------------- قائمة الدخل ----------------
    public async Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, DateTime? compareFrom, DateTime? compareTo, CancellationToken ct = default)
    {
        EnsurePeriod(from, to);
        var compare = compareFrom.HasValue && compareTo.HasValue;
        if (compare) EnsurePeriod(compareFrom!.Value, compareTo!.Value);

        var accounts = await AccountsAsync(ct);
        var current = await NetByAccountAsync(from.Date, to, excludeYearClosing: true, ct);
        var previous = compare ? await NetByAccountAsync(compareFrom!.Value.Date, compareTo!.Value, excludeYearClosing: true, ct) : null;
        static bool IsCostOfSales(Account a) => a.Type == AccountCategory.Expense && a.Code.StartsWith(CostOfSalesGroup, StringComparison.Ordinal);

        var statement = new IncomeStatementDto
        {
            From = from.Date, To = to.Date, CompareFrom = compare ? compareFrom!.Value.Date : null, CompareTo = compare ? compareTo!.Value.Date : null,
            Revenue = Section(accounts, current, previous, a => a.Type == AccountCategory.Revenue, -1),
            CostOfSales = Section(accounts, current, previous, IsCostOfSales, 1),
            OperatingExpenses = Section(accounts, current, previous, a => a.Type == AccountCategory.Expense && !IsCostOfSales(a), 1),
        };
        statement.GrossProfit = Total((statement.Revenue, 1), (statement.CostOfSales, -1));
        statement.NetProfit = Total((statement.GrossProfit, 1), (statement.OperatingExpenses, -1));
        return statement;
    }

    // ---------------- قائمة المركز المالي ----------------
    public async Task<FinancialPositionDto> GetFinancialPositionAsync(DateTime? asOf, DateTime? compareAsOf, CancellationToken ct = default)
    {
        var date = (asOf ?? DateTime.UtcNow).Date;
        var accounts = await AccountsAsync(ct);
        var current = await NetByAccountAsync(null, date, excludeYearClosing: false, ct);
        var previous = compareAsOf.HasValue ? await NetByAccountAsync(null, compareAsOf.Value, excludeYearClosing: false, ct) : null;
        static bool IsNonCurrent(Account a) => a.Code.StartsWith(NonCurrentAssetsGroup, StringComparison.Ordinal);

        var position = new FinancialPositionDto
        {
            AsOf = date, CompareAsOf = compareAsOf?.Date,
            CurrentAssets = Section(accounts, current, previous, a => a.Type == AccountCategory.Asset && !IsNonCurrent(a), 1),
            NonCurrentAssets = Section(accounts, current, previous, a => a.Type == AccountCategory.Asset && IsNonCurrent(a), 1),
            Liabilities = Section(accounts, current, previous, a => a.Type == AccountCategory.Liability, -1),
            Equity = Section(accounts, current, previous, a => a.Type == AccountCategory.Equity, -1),
        };
        // الإيرادات والمصروفات غير المقفلة: نتيجة تُضاف لحقوق الملكية حتى يُقفلها قيد إقفال السنة
        var unclosed = Section(accounts, current, previous, a => a.Type is AccountCategory.Revenue or AccountCategory.Expense, -1);
        position.UnclosedProfit = new StatementSectionDto { Total = unclosed.Total, Comparative = unclosed.Comparative };
        position.TotalAssets = Total((position.CurrentAssets, 1), (position.NonCurrentAssets, 1));
        position.TotalLiabilitiesAndEquity = Total((position.Liabilities, 1), (position.Equity, 1), (position.UnclosedProfit, 1));
        position.IsBalanced = Math.Abs(position.TotalAssets.Total - position.TotalLiabilitiesAndEquity.Total) < 0.01m;
        return position;
    }

    // ---------------- قائمة التدفقات النقدية ----------------
    public async Task<CashFlowDto> GetCashFlowAsync(DateTime from, DateTime to, DateTime? compareFrom, DateTime? compareTo, CancellationToken ct = default)
    {
        EnsurePeriod(from, to);
        var compare = compareFrom.HasValue && compareTo.HasValue;
        if (compare) EnsurePeriod(compareFrom!.Value, compareTo!.Value);

        var accounts = await AccountsAsync(ct);
        var current = await CashCounterpartsAsync(from.Date, to, ct);
        var previous = compare ? await CashCounterpartsAsync(compareFrom!.Value.Date, compareTo!.Value, ct) : null;

        static bool IsInvesting(Account a) => a.Type == AccountCategory.Asset && a.Code.StartsWith(NonCurrentAssetsGroup, StringComparison.Ordinal);
        static bool IsFinancing(Account a) => a.Type == AccountCategory.Equity
            || (a.Type == AccountCategory.Liability && a.Code.StartsWith(LongTermLiabilitiesGroup, StringComparison.Ordinal));

        var flow = new CashFlowDto
        {
            From = from.Date, To = to.Date, CompareFrom = compare ? compareFrom!.Value.Date : null, CompareTo = compare ? compareTo!.Value.Date : null,
            // الحساب المقابل الدائن مصدر نقد (+) والمدين استخدام له (−)
            Operating = Section(accounts, current, previous, a => !IsInvesting(a) && !IsFinancing(a), -1),
            Investing = Section(accounts, current, previous, IsInvesting, -1),
            Financing = Section(accounts, current, previous, IsFinancing, -1),
            OpeningCash = new StatementSectionDto
            {
                Total = await CashBalanceAsync(from.Date.AddDays(-1), ct),
                Comparative = compare ? await CashBalanceAsync(compareFrom!.Value.Date.AddDays(-1), ct) : 0,
            },
        };
        flow.NetChange = Total((flow.Operating, 1), (flow.Investing, 1), (flow.Financing, 1));
        flow.ClosingCash = Total((flow.OpeningCash, 1), (flow.NetChange, 1));
        return flow;
    }

    private static bool IsCash(string code) => code.StartsWith(DefaultAccounts.Banks, StringComparison.Ordinal);

    /// <summary>
    /// السطور غير النقدية في القيود التي حرّكت النقدية خلال الفترة: مجموع (دائن − مدين) لها يساوي صافي حركة النقد
    /// في القيد نفسه (القيد متوازن)، فتُنسب كل حركة نقد إلى حسابها المقابل بدقة.
    /// </summary>
    private async Task<List<Movement>> CashCounterpartsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var end = to.Date.AddDays(1);
        var lines = _db.Set<JournalEntryLine>().AsNoTracking();
        var cashEntryIds = from l in lines
                           join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                           where e.Date >= @from && e.Date < end && l.AccountCode.StartsWith(DefaultAccounts.Banks)
                           select e.Id;
        var counterparts = await lines.Where(l => cashEntryIds.Contains(l.JournalEntryId) && !l.AccountCode.StartsWith(DefaultAccounts.Banks))
            .GroupBy(l => l.AccountCode).Select(g => new { Code = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).ToListAsync(ct);
        return counterparts.Select(c => new Movement(c.Code, c.Debit - c.Credit)).ToList();
    }

    private async Task<decimal> CashBalanceAsync(DateTime asOf, CancellationToken ct)
        => DocumentPricing.Round((await NetByAccountAsync(null, asOf, excludeYearClosing: false, ct)).Where(m => IsCash(m.Code)).Sum(m => m.Net));
}
