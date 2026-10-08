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
            ?? throw new NotFoundException(Messages.AccountNotFound);

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

    public async Task<FinancialStatsDto> GetFinancialStatsAsync(CancellationToken ct = default)
    {
        var invoices = await _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted")
            .Select(i => new { i.Kind, Subtotal = i.Subtotal * i.ExchangeRate, i.TotalCost }).ToListAsync(ct); // بالعملة الأساسية
        var sales = invoices.Where(i => i.Kind == InvoiceKind.Sales).Sum(i => i.Subtotal) - invoices.Where(i => i.Kind == InvoiceKind.SalesReturn).Sum(i => i.Subtotal);
        var purchases = invoices.Where(i => i.Kind == InvoiceKind.Purchase).Sum(i => i.Subtotal) - invoices.Where(i => i.Kind == InvoiceKind.PurchaseReturn).Sum(i => i.Subtotal);
        var cogs = invoices.Where(i => i.Kind == InvoiceKind.Sales).Sum(i => i.TotalCost) - invoices.Where(i => i.Kind == InvoiceKind.SalesReturn).Sum(i => i.TotalCost);

        var vouchers = await _db.Set<Voucher>().AsNoTracking().GroupBy(v => v.Type).Select(g => new { g.Key, Sum = g.Sum(v => v.Amount) }).ToListAsync(ct);
        var leaves = await LeavesAsync(ct);
        decimal Under(string prefix) => leaves.Where(a => a.Code.StartsWith(prefix)).Sum(a => a.Balance);

        var opex = Under("52");
        var outputVat = Under(DefaultAccounts.OutputVat); var inputVat = Under(DefaultAccounts.InputVat);
        var inventory = await _db.Set<Product>().AsNoTracking().SumAsync(p => p.CurrentStock * p.AverageCost, ct);

        return new FinancialStatsDto
        {
            TotalSales = sales, TotalPurchases = purchases,
            TotalReceipts = vouchers.FirstOrDefault(v => v.Key == VoucherType.Receipt)?.Sum ?? 0,
            TotalPayments = vouchers.FirstOrDefault(v => v.Key == VoucherType.Payment)?.Sum ?? 0,
            CogsTotal = cogs, OperatingExpenses = opex, NetProfit = sales - cogs - opex,
            InventoryValuation = inventory, OutputVat = outputVat, InputVat = inputVat, NetVatPayable = outputVat - inputVat,
            CashAndBankBalance = Under(DefaultAccounts.Banks), ReceivablesBalance = Under(DefaultAccounts.Receivables), PayablesBalance = Under(DefaultAccounts.Payables),
        };
    }

    public async Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime? asOf, CancellationToken ct = default)
    {
        var date = (asOf ?? DateTime.UtcNow).Date;
        var end = date.AddDays(1);
        var movements = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                               join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                               where e.Date < end
                               group l by l.AccountCode into g
                               select new { Code = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) }).ToListAsync(ct);
        var accounts = await _db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Code, ct);

        var sheet = new BalanceSheetDto { AsOf = date };
        foreach (var m in movements.Where(m => m.Net != 0 && accounts.ContainsKey(m.Code)).OrderBy(m => m.Code))
        {
            var account = accounts[m.Code];
            BalanceSheetLineDto Line(decimal amount) => new() { AccountCode = account.Code, NameAr = account.NameAr, NameEn = account.NameEn, Amount = amount };
            switch (account.Type)
            {
                case AccountCategory.Asset: sheet.Assets.Add(Line(m.Net)); break;
                case AccountCategory.Liability: sheet.Liabilities.Add(Line(-m.Net)); break;
                case AccountCategory.Equity: sheet.Equity.Add(Line(-m.Net)); break;
                default: sheet.NetProfit -= m.Net; break; // إيراد دائن يزيد النتيجة ومصروف مدين ينقصها
            }
        }
        sheet.TotalAssets = sheet.Assets.Sum(l => l.Amount);
        sheet.TotalLiabilities = sheet.Liabilities.Sum(l => l.Amount);
        sheet.TotalEquity = sheet.Equity.Sum(l => l.Amount);
        sheet.TotalLiabilitiesAndEquity = sheet.TotalLiabilities + sheet.TotalEquity + sheet.NetProfit;
        sheet.IsBalanced = Math.Abs(sheet.TotalAssets - sheet.TotalLiabilitiesAndEquity) < 0.01m;
        return sheet;
    }

    /// <summary>
    /// تسويات الضريبة بقيود يدوية في الفترة: ما قُيِّد يدوياً على حسابي ضريبة المخرجات والمدخلات.
    /// قيد سداد الضريبة للهيئة أو مقاصّتها ليس تسوية: يُعرف بأن سطوره الأخرى كلها نقدية/بنوك أو حسابات ضريبة، فيُستثنى.
    /// </summary>
    private async Task<(decimal Output, decimal Input)> ManualVatAdjustmentsAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        var vatAccounts = new[] { DefaultAccounts.OutputVat, DefaultAccounts.InputVat };
        // القيد اليدوي وعكسه (العكس يُلغي أثر التسوية في فترته)
        var entries = _db.Set<JournalEntry>().AsNoTracking();
        var entryIds = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                              join e in entries on l.JournalEntryId equals e.Id
                              where e.Date >= start && e.Date < end && vatAccounts.Contains(l.AccountCode)
                                    && (e.SourceType == null || e.SourceType == "manual"
                                        || (e.SourceType == "reversal" && entries.Any(o => o.Id == e.SourceReferenceId && (o.SourceType == null || o.SourceType == "manual"))))
                              select e.Id).Distinct().ToListAsync(ct);
        if (entryIds.Count == 0) return (0, 0);

        var entryLines = await _db.Set<JournalEntryLine>().AsNoTracking().Where(l => entryIds.Contains(l.JournalEntryId))
            .Select(l => new { l.JournalEntryId, l.AccountCode, l.Debit, l.Credit }).ToListAsync(ct);
        decimal output = 0, input = 0;
        foreach (var entry in entryLines.GroupBy(l => l.JournalEntryId))
        {
            var others = entry.Where(l => !vatAccounts.Contains(l.AccountCode)).ToList();
            if (others.All(l => l.AccountCode.StartsWith(DefaultAccounts.Banks))) continue; // سداد للهيئة/استرداد منها أو مقاصّة بين الحسابين
            output += entry.Where(l => l.AccountCode == DefaultAccounts.OutputVat).Sum(l => l.Credit - l.Debit);
            input += entry.Where(l => l.AccountCode == DefaultAccounts.InputVat).Sum(l => l.Debit - l.Credit);
        }
        return (DocumentPricing.Round(output), DocumentPricing.Round(input));
    }

    public async Task<VatReturnDto> GetVatReturnAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (to < from) throw new ValidationFailedException(Messages.PeriodEndBeforeStart);
        // من سطور الفواتير المرحّلة في الفترة: كل سطر بتصنيفه الضريبي، محوَّلاً للعملة الأساسية، والمرتجعات تُطرح
        var start = from.Date; var end = to.Date.AddDays(1);
        var lines = await (from item in _db.Set<InvoiceItem>().AsNoTracking()
                           join i in _db.Set<Invoice>().AsNoTracking() on item.InvoiceId equals i.Id
                           where i.Status == "posted" && i.IssueDate >= start && i.IssueDate < end
                           select new { i.Kind, i.ExchangeRate, item.VatCategory, item.VatExemptionReasonCode, item.TotalBeforeVat, item.VatAmount }).ToListAsync(ct);

        // exports: null = لا تمييز، true = الصادرات فقط، false = ما عداها
        decimal Sum(bool sales, VatCategory? category, bool vat, bool? exports = null)
        {
            decimal total = 0;
            foreach (var l in lines)
            {
                var isSales = l.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
                if (isSales != sales || (category.HasValue && l.VatCategory != category)) continue;
                if (exports.HasValue && VatExemptionReasons.ExportCodes.Contains(l.VatExemptionReasonCode) != exports) continue;
                var sign = l.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn ? -1 : 1;
                total += sign * (vat ? l.VatAmount : l.TotalBeforeVat) * (l.ExchangeRate <= 0 ? 1 : l.ExchangeRate);
            }
            return DocumentPricing.Round(total);
        }

        // مصروفات وإيرادات مباشرة بضريبة مسجَّلة بسندات صرف/قبض (خاضعة للنسبة الأساسية)
        var vouchers = await _db.Set<Voucher>().AsNoTracking().Where(v => v.VatAmount > 0 && v.Date >= start && v.Date < end)
            .Select(v => new { v.Type, v.Amount, v.VatAmount }).ToListAsync(ct);
        decimal VoucherNet(VoucherType type) => vouchers.Where(v => v.Type == type).Sum(v => v.Amount - v.VatAmount);
        decimal VoucherVat(VoucherType type) => vouchers.Where(v => v.Type == type).Sum(v => v.VatAmount);

        var (outAdjustments, inAdjustments) = await ManualVatAdjustmentsAsync(start, end, ct);
        var outVat = Sum(true, null, vat: true) + VoucherVat(VoucherType.Receipt) + outAdjustments;
        var inVat = Sum(false, null, vat: true) + VoucherVat(VoucherType.Payment) + inAdjustments;
        return new VatReturnDto
        {
            FromDate = from, ToDate = to,
            StandardRatedSales = Sum(true, VatCategory.Standard, false) + VoucherNet(VoucherType.Receipt), ZeroRatedSales = Sum(true, VatCategory.ZeroRated, false, exports: false),
            ExportSales = Sum(true, VatCategory.ZeroRated, false, exports: true),
            ExemptSales = Sum(true, VatCategory.Exempt, false), OutOfScopeSales = Sum(true, VatCategory.OutOfScope, false),
            OutputVat = outVat, OutputVatAdjustments = outAdjustments,
            StandardRatedPurchases = Sum(false, VatCategory.Standard, false) + VoucherNet(VoucherType.Payment), ZeroRatedPurchases = Sum(false, VatCategory.ZeroRated, false),
            ExemptPurchases = Sum(false, VatCategory.Exempt, false), OutOfScopePurchases = Sum(false, VatCategory.OutOfScope, false),
            InputVat = inVat, InputVatAdjustments = inAdjustments,
            NetVatPayable = outVat - inVat,
        };
    }

    public async Task<LoadResult> LoadTrialBalanceAsync(DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(await GetTrialBalanceAsync(from, to, ct), options, ct);

    public async Task<LoadResult> LoadAccountStatementEntriesAsync(string accountCode, DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default)
        => await ReportLoader.LoadAsync((await GetAccountStatementAsync(accountCode, from, to, ct)).Entries, options, ct);

    public Task<LoadResult> LoadAccountBalancesAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => EntityLoader.LoadRowsAsync(_db.Set<Account>().AsNoTracking().Select(a => new AccountBalanceRowDto
        {
            Id = a.Id, Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, Type = a.Type, Level = a.Level,
            Balance = a.Balance, IsDebitNature = a.IsDebitNature,
            Debit = a.IsDebitNature ? Math.Abs(a.Balance) : 0m,
            Credit = a.IsDebitNature ? 0m : Math.Abs(a.Balance),
        }), options, nameof(AccountBalanceRowDto.Id), ct, EntityLoader.Asc(nameof(AccountBalanceRowDto.Code)));

    public Task<LoadResult> LoadJournalLedgerAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => EntityLoader.LoadRowsAsync(
            from l in _db.Set<JournalEntryLine>().AsNoTracking()
            join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
            select new JournalLedgerRowDto
            {
                LineId = l.Id, EntryId = e.Id, EntryNumber = e.EntryNumber, Date = e.Date, Description = e.Description,
                AccountCode = l.AccountCode, AccountName = l.AccountName, Debit = l.Debit, Credit = l.Credit, ReferenceType = e.ReferenceType,
            }, options, nameof(JournalLedgerRowDto.LineId), ct,
            EntityLoader.Desc(nameof(JournalLedgerRowDto.Date)), EntityLoader.Desc(nameof(JournalLedgerRowDto.EntryNumber)));
}
