using ERP.Service.Services.Shared;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Accounting;

public class AccountingReportService : IAccountingReportService
{
    private readonly ErpDbContext _db;
    public AccountingReportService(ErpDbContext db) => _db = db;

    public async Task<List<TrialBalanceItemDto>> GetTrialBalanceAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var lines = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                           join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                           select new { l.AccountCode, l.Debit, l.Credit, e.Date }).ToListAsync(ct);
        var accounts = await _db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Code, ct);

        var rows = new List<TrialBalanceItemDto>();
        foreach (var g in lines.GroupBy(l => l.AccountCode).OrderBy(g => g.Key))
        {
            if (to.HasValue && g.All(l => l.Date > to)) continue;
            var opening = from.HasValue ? g.Where(l => l.Date < from).ToList() : new();
            var period = g.Where(l => (!from.HasValue || l.Date >= from) && (!to.HasValue || l.Date <= to)).ToList();
            var openNet = opening.Sum(l => l.Debit) - opening.Sum(l => l.Credit);
            var pd = period.Sum(l => l.Debit); var pc = period.Sum(l => l.Credit);
            var closeNet = openNet + pd - pc;
            accounts.TryGetValue(g.Key, out var acc);
            rows.Add(new TrialBalanceItemDto
            {
                AccountCode = g.Key, AccountNameAr = acc?.NameAr ?? g.Key, AccountCategory = acc?.Type ?? AccountCategory.Asset,
                OpeningDebit = openNet > 0 ? openNet : 0, OpeningCredit = openNet < 0 ? -openNet : 0,
                PeriodDebit = pd, PeriodCredit = pc,
                ClosingDebit = closeNet > 0 ? closeNet : 0, ClosingCredit = closeNet < 0 ? -closeNet : 0,
            });
        }
        return rows;
    }

    public async Task<AccountStatementDto> GetAccountStatementAsync(string accountCode, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var account = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == accountCode, ct)
            ?? throw new NotFoundException("الحساب غير موجود");

        var q = from l in _db.Set<JournalEntryLine>().AsNoTracking()
                join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                where l.AccountCode == accountCode
                select new { l.Debit, l.Credit, e.Date, e.EntryNumber, e.Description, e.ReferenceNumber, l.Notes };
        var all = await q.OrderBy(x => x.Date).ThenBy(x => x.EntryNumber).ToListAsync(ct);

        decimal Signed(decimal d, decimal c) => account.IsDebitNature ? d - c : c - d;
        var opening = from.HasValue ? all.Where(x => x.Date < from).Sum(x => Signed(x.Debit, x.Credit)) : 0m;
        var running = opening;
        var dto = new AccountStatementDto { AccountCode = account.Code, AccountNameAr = account.NameAr, OpeningBalance = opening };
        foreach (var x in all.Where(x => (!from.HasValue || x.Date >= from) && (!to.HasValue || x.Date <= to)))
        {
            running += Signed(x.Debit, x.Credit);
            dto.Entries.Add(new AccountLedgerEntryDto
            {
                Date = x.Date, EntryNumber = x.EntryNumber, Description = string.IsNullOrWhiteSpace(x.Notes) ? x.Description : x.Notes!,
                Debit = x.Debit, Credit = x.Credit, RunningBalance = running, ReferenceNumber = x.ReferenceNumber,
            });
        }
        dto.ClosingBalance = running;
        return dto;
    }

    private async Task<List<Account>> LeavesAsync(CancellationToken ct)
    {
        var all = await _db.Set<Account>().AsNoTracking().ToListAsync(ct);
        var parents = all.Where(a => a.ParentCode != null).Select(a => a.ParentCode!).ToHashSet();
        return all.Where(a => !parents.Contains(a.Code)).ToList();
    }

    public async Task<FinancialSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default)
    {
        var leaves = await LeavesAsync(ct);
        decimal Sum(AccountCategory t) => leaves.Where(a => a.Type == t).Sum(a => a.Balance);
        var assets = Sum(AccountCategory.Asset); var liab = Sum(AccountCategory.Liability); var eq = Sum(AccountCategory.Equity);
        var rev = Sum(AccountCategory.Revenue); var exp = Sum(AccountCategory.Expense);
        var net = rev - exp;
        return new FinancialSummaryDto
        {
            TotalAssets = assets, TotalLiabilities = liab, TotalEquity = eq, TotalRevenues = rev, TotalExpenses = exp,
            NetProfitOrLoss = net, IsBalanceSheetBalanced = Math.Abs(assets - (liab + eq + net)) < 0.01m,
        };
    }

    /// <summary>إجماليات الفواتير المرحّلة لكل نوع مستند (≤ 4 صفوف) بتجميع SQL واحد.</summary>
    private sealed record KindTotals(InvoiceKind Kind, int Count, decimal Subtotal, decimal TotalCost, decimal VatTotal, decimal GrandTotal, decimal CreditSubtotal);

    private async Task<Dictionary<InvoiceKind, KindTotals>> PostedTotalsByKindAsync(CancellationToken ct)
        => (await _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted")
                .GroupBy(i => i.Kind)
                .Select(g => new KindTotals(g.Key, g.Count(), g.Sum(i => i.Subtotal), g.Sum(i => i.TotalCost), g.Sum(i => i.VatTotal), g.Sum(i => i.GrandTotal),
                    g.Sum(i => i.PaymentMethod == PaymentMethod.Credit ? i.Subtotal : 0)))
                .ToListAsync(ct))
            .ToDictionary(t => t.Kind);

    private static KindTotals Of(Dictionary<InvoiceKind, KindTotals> totals, InvoiceKind kind)
        => totals.GetValueOrDefault(kind) ?? new KindTotals(kind, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// مؤشرات مالية بتجميع SQL (لا تحميل للفواتير أو الحسابات في الذاكرة). الصيغ كما كانت:
    /// المبيعات/المشتريات/التكلفة = صافي المرحّل بعد المرتجعات بلا ضريبة؛ أرصدة الحسابات = Σ أرصدة الحسابات الطرفية تحت البادئة.
    /// </summary>
    public async Task<FinancialStatsDto> GetFinancialStatsAsync(CancellationToken ct = default)
    {
        var t = await PostedTotalsByKindAsync(ct);
        var sales = Of(t, InvoiceKind.Sales).Subtotal - Of(t, InvoiceKind.SalesReturn).Subtotal;
        var purchases = Of(t, InvoiceKind.Purchase).Subtotal - Of(t, InvoiceKind.PurchaseReturn).Subtotal;
        var cogs = Of(t, InvoiceKind.Sales).TotalCost - Of(t, InvoiceKind.SalesReturn).TotalCost;

        var vouchers = await _db.Set<Voucher>().AsNoTracking().GroupBy(v => v.Type).Select(g => new { g.Key, Sum = g.Sum(v => v.Amount) }).ToListAsync(ct);

        // الحساب الطرفي = لا حساب فرعي يشير إليه؛ المجاميع حسب البادئة تُحسب في استعلام واحد
        var accounts = _db.Set<Account>().AsNoTracking();
        var b = await accounts.Where(a => !accounts.Any(c => c.ParentCode == a.Code)).GroupBy(_ => 1).Select(g => new
        {
            Opex = g.Sum(a => a.Code.StartsWith("52") ? a.Balance : 0),
            OutputVat = g.Sum(a => a.Code.StartsWith(DefaultAccounts.OutputVat) ? a.Balance : 0),
            InputVat = g.Sum(a => a.Code.StartsWith(DefaultAccounts.InputVat) ? a.Balance : 0),
            Banks = g.Sum(a => a.Code.StartsWith(DefaultAccounts.Banks) ? a.Balance : 0),
            Receivables = g.Sum(a => a.Code.StartsWith(DefaultAccounts.Receivables) ? a.Balance : 0),
            Payables = g.Sum(a => a.Code.StartsWith(DefaultAccounts.Payables) ? a.Balance : 0),
        }).FirstOrDefaultAsync(ct);
        var inventory = await _db.Set<Product>().AsNoTracking().SumAsync(p => p.CurrentStock * p.AverageCost, ct);

        decimal opex = b?.Opex ?? 0, outputVat = b?.OutputVat ?? 0, inputVat = b?.InputVat ?? 0;
        return new FinancialStatsDto
        {
            TotalSales = sales, TotalPurchases = purchases,
            TotalReceipts = vouchers.FirstOrDefault(v => v.Key == VoucherType.Receipt)?.Sum ?? 0,
            TotalPayments = vouchers.FirstOrDefault(v => v.Key == VoucherType.Payment)?.Sum ?? 0,
            CogsTotal = cogs, OperatingExpenses = opex, NetProfit = sales - cogs - opex,
            InventoryValuation = inventory, OutputVat = outputVat, InputVat = inputVat, NetVatPayable = outputVat - inputVat,
            CashAndBankBalance = b?.Banks ?? 0, ReceivablesBalance = b?.Receivables ?? 0, PayablesBalance = b?.Payables ?? 0,
        };
    }

    /// <summary>مؤشرات لوحة التحكم (الصيغ موثّقة في <see cref="DashboardSummaryDto"/>).</summary>
    public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(int months = 9, CancellationToken ct = default)
    {
        if (months is < 1 or > 36) throw new ValidationFailedException("عدد الأشهر بين 1 و 36.");
        var t = await PostedTotalsByKindAsync(ct);
        KindTotals s = Of(t, InvoiceKind.Sales), sr = Of(t, InvoiceKind.SalesReturn), p = Of(t, InvoiceKind.Purchase), pr = Of(t, InvoiceKind.PurchaseReturn);

        var dto = new DashboardSummaryDto
        {
            NetSales = s.Subtotal - sr.Subtotal,
            NetCostOfSales = s.TotalCost - sr.TotalCost,
            NetOutputVat = s.VatTotal - sr.VatTotal,
            NetPurchases = p.Subtotal - pr.Subtotal,
            NetPurchasesWithVat = p.GrandTotal - pr.GrandTotal,
            NetInputVat = p.VatTotal - pr.VatTotal,
            NetCreditPurchases = p.CreditSubtotal - pr.CreditSubtotal,
            SalesCount = s.Count, SalesReturnsCount = sr.Count, PurchasesCount = p.Count, PurchaseReturnsCount = pr.Count,
            VouchersCount = await _db.Set<Voucher>().AsNoTracking().CountAsync(ct),
            PostedJournalEntriesCount = await _db.Set<JournalEntry>().AsNoTracking().CountAsync(e => e.Status == JournalEntryStatus.Posted, ct),
        };
        dto.GrossProfit = dto.NetSales - dto.NetCostOfSales;
        dto.NetCashPurchases = dto.NetPurchases - dto.NetCreditPurchases;

        // الاتجاه الشهري: تجميع SQL حسب (السنة، الشهر، النوع) ثم ملء الأشهر الخالية بأصفار
        var now = DateTime.UtcNow;
        var since = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));
        var monthly = await _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted" && i.IssueDate >= since)
            .GroupBy(i => new { i.IssueDate.Year, i.IssueDate.Month, i.Kind })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Kind, Subtotal = g.Sum(i => i.Subtotal), Cost = g.Sum(i => i.TotalCost) })
            .ToListAsync(ct);
        for (var m = 0; m < months; m++)
        {
            var d = since.AddMonths(m);
            decimal Sum(InvoiceKind k, bool cost) => monthly.Where(x => x.Year == d.Year && x.Month == d.Month && x.Kind == k).Sum(x => cost ? x.Cost : x.Subtotal);
            var netSales = Sum(InvoiceKind.Sales, false) - Sum(InvoiceKind.SalesReturn, false);
            var netCost = Sum(InvoiceKind.Sales, true) - Sum(InvoiceKind.SalesReturn, true);
            dto.Monthly.Add(new DashboardMonthDto
            {
                Year = d.Year, Month = d.Month, NetSales = netSales, GrossProfit = netSales - netCost,
                NetPurchases = Sum(InvoiceKind.Purchase, false) - Sum(InvoiceKind.PurchaseReturn, false),
            });
        }
        return dto;
    }

    // ---------- دفتر اليومية العام (أسطر القيود) ----------

    private IQueryable<JournalLedgerRowDto> JournalLedgerQuery(ReportQueryDto q)
    {
        var entries = _db.Set<JournalEntry>().AsNoTracking();
        if (q.DateFrom.HasValue) entries = entries.Where(e => e.Date >= q.DateFrom);
        if (ListQuery.EndExclusive(q.DateTo) is { } end) entries = entries.Where(e => e.Date < end);
        if (q.CostCenterId.HasValue) entries = entries.Where(e => e.Lines.Any(l => l.CostCenterId == q.CostCenterId));
        return entries.SelectMany(e => e.Lines, (e, l) => new JournalLedgerRowDto
            {
                Id = l.Id, JournalEntryId = e.Id, Date = e.Date, EntryNumber = e.EntryNumber, Description = e.Description,
                ReferenceType = e.ReferenceType, ReferenceNumber = e.ReferenceNumber, Status = e.Status,
                AccountCode = l.AccountCode, AccountName = l.AccountName, CostCenterId = l.CostCenterId, Debit = l.Debit, Credit = l.Credit, Notes = l.Notes,
            })
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.EntryNumber).ThenBy(r => r.Id);
    }

    public Task<LoadResult> LoadJournalLedgerAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(JournalLedgerQuery(q), options, ct, "-Date", "-EntryNumber", "Id");

    // ---------- توزيع مدفوعات المبيعات ----------

    /// <summary>
    /// فواتير البيع غير المرتبطة بأسطر مركبات (نفس نطاق شاشة المبيعات القياسية التي بُني عليها التقرير).
    /// القواعد موثّقة في <see cref="SalesPaymentBreakdownRowDto"/>.
    /// </summary>
    private IQueryable<SalesPaymentBreakdownRowDto> SalesPaymentBreakdownQuery(ReportQueryDto q)
    {
        var customers = _db.Set<Customer>().AsNoTracking();
        var inv = _db.Set<Invoice>().AsNoTracking().Where(i => i.Kind == InvoiceKind.Sales && !i.VehicleLines.Any());
        if (q.DateFrom.HasValue) inv = inv.Where(i => i.IssueDate >= q.DateFrom);
        if (ListQuery.EndExclusive(q.DateTo) is { } end) inv = inv.Where(i => i.IssueDate < end);

        return inv
            .Select(i => new
            {
                i,
                HasSplits = i.PaymentSplits.Any(),
                // بلا أسطر مجزأة: الفاتورة غير الآجلة والمعتمدة مدفوعة بكامل قيمتها بطريقتها
                Direct = i.PaymentMethod != PaymentMethod.Credit && i.Status != "draft" && i.Status != "cancelled" ? i.GrandTotal : 0m,
                Cust = customers.Where(c => c.Id == i.PartyId).Select(c => new { c.Phone, c.Email, c.City, c.VatNumber }).FirstOrDefault(),
            })
            .Select(x => new
            {
                x.i, x.Cust,
                Cash = x.HasSplits ? x.i.PaymentSplits.Where(s => s.Method == PaymentMethod.Cash).Sum(s => (decimal?)s.Amount) ?? 0 : (x.i.PaymentMethod == PaymentMethod.Cash ? x.Direct : 0),
                Card = x.HasSplits ? x.i.PaymentSplits.Where(s => s.Method == PaymentMethod.BankCard).Sum(s => (decimal?)s.Amount) ?? 0 : (x.i.PaymentMethod == PaymentMethod.BankCard ? x.Direct : 0),
                Transfer = x.HasSplits ? x.i.PaymentSplits.Where(s => s.Method == PaymentMethod.BankTransfer).Sum(s => (decimal?)s.Amount) ?? 0 : (x.i.PaymentMethod == PaymentMethod.BankTransfer ? x.Direct : 0),
                Credit = x.HasSplits ? x.i.PaymentSplits.Where(s => s.Method == PaymentMethod.Credit).Sum(s => (decimal?)s.Amount) ?? 0 : 0,
            })
            .Select(y => new SalesPaymentBreakdownRowDto
            {
                Id = y.i.Id, Number = y.i.InvoiceNumber, Date = y.i.IssueDate, Time = y.i.IssueTime, InvoiceStatus = y.i.Status,
                Customer = y.i.PartyName,
                Phone = y.i.PartyPhone != null && y.i.PartyPhone != "" ? y.i.PartyPhone : y.Cust!.Phone,
                Email = y.i.PartyEmail != null && y.i.PartyEmail != "" ? y.i.PartyEmail : y.Cust!.Email,
                Address = y.i.PartyAddress != null && y.i.PartyAddress != "" ? y.i.PartyAddress : y.Cust!.City,
                VatNumber = y.i.PartyVatNumber != null && y.i.PartyVatNumber != "" ? y.i.PartyVatNumber : y.Cust!.VatNumber,
                ItemQuantity = y.i.Items.Sum(it => (decimal?)it.Quantity) ?? 0,
                Subtotal = y.i.Subtotal, Discounts = y.i.DiscountTotal, Tax = y.i.VatTotal, Grand = y.i.GrandTotal,
                PaidCash = y.Cash, PaidBankCard = y.Card, PaidBankTransfer = y.Transfer, PaidCredit = y.Credit,
                Paid = y.Cash + y.Card + y.Transfer + y.Credit,
                Balance = y.i.GrandTotal - (y.Cash + y.Card + y.Transfer + y.Credit) > 0 ? y.i.GrandTotal - (y.Cash + y.Card + y.Transfer + y.Credit) : 0,
                PaymentStatus = y.i.GrandTotal - (y.Cash + y.Card + y.Transfer + y.Credit) <= 0 ? "paid"
                    : y.Cash + y.Card + y.Transfer + y.Credit > 0 ? "partial" : "unpaid",
            })
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Time).ThenByDescending(r => r.Number).ThenBy(r => r.Id);
    }

    public Task<LoadResult> LoadSalesPaymentBreakdownAsync(ReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default)
        => ReportLoader.LoadAsync(SalesPaymentBreakdownQuery(q), options, ct, "-Date", "-Time", "-Number", "Id");

    public async Task<VatReturnDto> GetVatReturnAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (to < from) throw new ValidationFailedException("نهاية الفترة قبل بدايتها.");
        var inv = await _db.Set<Invoice>().AsNoTracking()
            .Where(i => i.Status == "posted" && i.IssueDate >= from && i.IssueDate <= to)
            .Select(i => new { i.Kind, i.Subtotal, i.VatTotal }).ToListAsync(ct);
        var sSales = inv.Where(i => i.Kind == InvoiceKind.Sales).ToList(); var sRet = inv.Where(i => i.Kind == InvoiceKind.SalesReturn).ToList();
        var pPur = inv.Where(i => i.Kind == InvoiceKind.Purchase).ToList(); var pRet = inv.Where(i => i.Kind == InvoiceKind.PurchaseReturn).ToList();
        var outVat = sSales.Sum(i => i.VatTotal) - sRet.Sum(i => i.VatTotal);
        var inVat = pPur.Sum(i => i.VatTotal) - pRet.Sum(i => i.VatTotal);
        return new VatReturnDto
        {
            FromDate = from, ToDate = to,
            StandardRatedSales = sSales.Sum(i => i.Subtotal) - sRet.Sum(i => i.Subtotal), OutputVat = outVat,
            StandardRatedPurchases = pPur.Sum(i => i.Subtotal) - pRet.Sum(i => i.Subtotal), InputVat = inVat,
            NetVatPayable = outVat - inVat,
        };
    }

    public async Task<LoadResult> LoadTrialBalanceAsync(DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetTrialBalanceAsync(from, to, ct), options, ct);

    public async Task<LoadResult> LoadAccountStatementEntriesAsync(string accountCode, DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync((await GetAccountStatementAsync(accountCode, from, to, ct)).Entries, options, ct);
}
