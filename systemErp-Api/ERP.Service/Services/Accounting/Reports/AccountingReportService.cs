using ERP.Service.Services.Shared;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Service.Services.Accounting;

public class AccountingReportService : IAccountingReportService
{
    private readonly ErpDbContext _db;
    public AccountingReportService(ErpDbContext db) => _db = db;

    private IQueryable<JournalEntryLine> Lines => _db.Set<JournalEntryLine>().AsNoTracking();
    private IQueryable<JournalEntry> Entries => _db.Set<JournalEntry>().AsNoTracking();
    private IQueryable<Account> Accounts => _db.Set<Account>().AsNoTracking();

    /// <summary>ميزان المراجعة: صف لكل حساب تحرّك حتى نهاية الفترة، مجمَّعاً في قاعدة البيانات.</summary>
    private IQueryable<TrialBalanceItemDto> TrialBalanceQuery(DateTime? from, DateTime? to, string? module)
    {
        // القيود تحمل وقت المستند: الفترة بالأيام، فنهايتها قبل بداية اليوم التالي
        DateTime? start = from?.Date, end = to?.Date.AddDays(1);
        var moduleScope = ReportModuleScope.Normalize(module);
        var hasStart = start.HasValue;
        var lines = from l in Lines
                    join e in Entries on l.JournalEntryId equals e.Id
                    where moduleScope == null || moduleScope.Contains(e.Module)
                    select new { l.AccountCode, l.Debit, l.Credit, e.Date };
        if (end.HasValue) lines = lines.Where(l => l.Date < end);

        var totals = from l in lines
                     group l by l.AccountCode into g
                     select new
                     {
                         Code = g.Key,
                         OpenNet = g.Sum(x => hasStart && x.Date < start ? x.Debit - x.Credit : 0m),
                         PeriodDebit = g.Sum(x => !hasStart || x.Date >= start ? x.Debit : 0m),
                         PeriodCredit = g.Sum(x => !hasStart || x.Date >= start ? x.Credit : 0m),
                     };
        return from t in totals
               join a in Accounts on t.Code equals a.Code into matched
               from a in matched.DefaultIfEmpty()
               select new { t, a, CloseNet = t.OpenNet + t.PeriodDebit - t.PeriodCredit } into r
               select new TrialBalanceItemDto
               {
                   AccountCode = r.t.Code, AccountNameAr = r.a.NameAr ?? r.t.Code, AccountCategory = (AccountCategory?)r.a.Type ?? AccountCategory.Asset,
                   OpeningDebit = r.t.OpenNet > 0 ? r.t.OpenNet : 0m, OpeningCredit = r.t.OpenNet < 0 ? -r.t.OpenNet : 0m,
                   PeriodDebit = r.t.PeriodDebit, PeriodCredit = r.t.PeriodCredit,
                   ClosingDebit = r.CloseNet > 0 ? r.CloseNet : 0m, ClosingCredit = r.CloseNet < 0 ? -r.CloseNet : 0m,
               };
    }

    private sealed class LedgerLine
    {
        public Guid Id { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public DateTime Date { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
        public string? Notes { get; set; }
    }

    private IQueryable<LedgerLine> AccountLines(string accountCode, string? module)
    {
        var moduleScope = ReportModuleScope.Normalize(module);
        return from l in Lines
               join e in Entries on l.JournalEntryId equals e.Id
               where l.AccountCode == accountCode && (moduleScope == null || moduleScope.Contains(e.Module))
               select new LedgerLine
               {
                   Id = l.Id, Debit = l.Debit, Credit = l.Credit, Date = e.Date, EntryNumber = e.EntryNumber,
                   Description = e.Description, ReferenceNumber = e.ReferenceNumber, Notes = l.Notes,
               };
    }

    /// <summary>الفترة بالأيام شاملةً يومَي البداية والنهاية كاملين (القيود تحمل وقت المستند).</summary>
    private static IQueryable<LedgerLine> InPeriod(IQueryable<LedgerLine> lines, DateTime? from, DateTime? to)
    {
        if (from.HasValue) { var start = from.Value.Date; lines = lines.Where(x => x.Date >= start); }
        if (to.HasValue) { var end = to.Value.Date.AddDays(1); lines = lines.Where(x => x.Date < end); }
        return lines;
    }

    private async Task<Account> AccountAsync(string accountCode, CancellationToken ct)
        => await Accounts.FirstOrDefaultAsync(a => a.Code == accountCode, ct) ?? throw new NotFoundException(Messages.AccountNotFound);

    /// <summary>
    /// حركات الحساب في الفترة، والرصيد الجاري لكل حركة يُحسب في قاعدة البيانات: مجموع كل ما سبقها (ومنه ما قبل الفترة)
    /// بترتيب التاريخ ثم رقم القيد ثم السطر.
    /// </summary>
    private IQueryable<AccountLedgerEntryDto> StatementEntries(Account account, DateTime? from, DateTime? to, string? module)
    {
        var debitNature = account.IsDebitNature;
        var all = AccountLines(account.Code, module);
        return InPeriod(all, from, to).Select(x => new AccountLedgerEntryDto
        {
            Id = x.Id, Date = x.Date, EntryNumber = x.EntryNumber, Description = string.IsNullOrWhiteSpace(x.Notes) ? x.Description : x.Notes!,
            Debit = x.Debit, Credit = x.Credit, ReferenceNumber = x.ReferenceNumber,
            RunningBalance = all.Where(p => p.Date < x.Date || (p.Date == x.Date
                    && (p.EntryNumber.CompareTo(x.EntryNumber) < 0 || (p.EntryNumber == x.EntryNumber && p.Id.CompareTo(x.Id) <= 0))))
                .Sum(p => debitNature ? p.Debit - p.Credit : p.Credit - p.Debit),
        });
    }

    public async Task<AccountStatementDto> GetAccountStatementAsync(string accountCode, DateTime? from, DateTime? to, string? module = null, CancellationToken ct = default)
    {
        var account = await AccountAsync(accountCode, ct);
        var debitNature = account.IsDebitNature;
        var all = AccountLines(account.Code, module);
        var start = from?.Date;
        var opening = start.HasValue ? await all.Where(x => x.Date < start).SumAsync(x => debitNature ? x.Debit - x.Credit : x.Credit - x.Debit, ct) : 0m;
        var movement = await InPeriod(all, from, to).SumAsync(x => debitNature ? x.Debit - x.Credit : x.Credit - x.Debit, ct);
        return new AccountStatementDto
        {
            AccountCode = account.Code, AccountNameAr = account.NameAr, OpeningBalance = opening, ClosingBalance = opening + movement,
            Entries = await StatementEntries(account, from, to, module).OrderBy(x => x.Date).ThenBy(x => x.EntryNumber).ThenBy(x => x.Id).ToListAsync(ct),
        };
    }

    /// <summary>الحسابات الطرفية (لا أبناء لها): أرصدتها وحدها تُجمع حتى لا يُحسب الرصيد مرتين.</summary>
    private IQueryable<Account> Leaves() => Accounts.Where(a => !Accounts.Any(c => c.ParentCode == a.Code));

    public async Task<FinancialSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default)
    {
        var byType = await Leaves().GroupBy(a => a.Type).Select(g => new { Type = g.Key, Balance = g.Sum(a => a.Balance) }).ToListAsync(ct);
        decimal Sum(AccountCategory t) => byType.Where(a => a.Type == t).Sum(a => a.Balance);
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
        var invoices = await _db.Set<Invoice>().AsNoTracking().Where(i => i.Status == "posted").GroupBy(i => i.Kind)
            .Select(g => new { Kind = g.Key, Subtotal = g.Sum(i => i.Subtotal * i.ExchangeRate), Cost = g.Sum(i => i.TotalCost) }).ToListAsync(ct); // بالعملة الأساسية
        decimal Subtotal(InvoiceKind kind) => invoices.Where(i => i.Kind == kind).Sum(i => i.Subtotal);
        decimal Cost(InvoiceKind kind) => invoices.Where(i => i.Kind == kind).Sum(i => i.Cost);
        var sales = Subtotal(InvoiceKind.Sales) - Subtotal(InvoiceKind.SalesReturn);
        var purchases = Subtotal(InvoiceKind.Purchase) - Subtotal(InvoiceKind.PurchaseReturn);
        var cogs = Cost(InvoiceKind.Sales) - Cost(InvoiceKind.SalesReturn);

        var vouchers = await _db.Set<Voucher>().AsNoTracking().GroupBy(v => v.Type).Select(g => new { g.Key, Sum = g.Sum(v => v.Amount) }).ToListAsync(ct);
        var under = await Leaves().GroupBy(a => 1).Select(g => new
        {
            Opex = g.Sum(a => a.Code.StartsWith("52") ? a.Balance : 0m),
            OutputVat = g.Sum(a => a.Code.StartsWith(DefaultAccounts.OutputVat) ? a.Balance : 0m),
            InputVat = g.Sum(a => a.Code.StartsWith(DefaultAccounts.InputVat) ? a.Balance : 0m),
            Banks = g.Sum(a => a.Code.StartsWith(DefaultAccounts.Banks) ? a.Balance : 0m),
            Receivables = g.Sum(a => a.Code.StartsWith(DefaultAccounts.Receivables) ? a.Balance : 0m),
            Payables = g.Sum(a => a.Code.StartsWith(DefaultAccounts.Payables) ? a.Balance : 0m),
        }).FirstOrDefaultAsync(ct);

        var opex = under?.Opex ?? 0;
        var outputVat = under?.OutputVat ?? 0; var inputVat = under?.InputVat ?? 0;
        var inventory = await _db.Set<Product>().AsNoTracking().SumAsync(p => p.CurrentStock * p.AverageCost, ct);

        return new FinancialStatsDto
        {
            TotalSales = sales, TotalPurchases = purchases,
            TotalReceipts = vouchers.FirstOrDefault(v => v.Key == VoucherType.Receipt)?.Sum ?? 0,
            TotalPayments = vouchers.FirstOrDefault(v => v.Key == VoucherType.Payment)?.Sum ?? 0,
            CogsTotal = cogs, OperatingExpenses = opex, NetProfit = sales - cogs - opex,
            InventoryValuation = inventory, OutputVat = outputVat, InputVat = inputVat, NetVatPayable = outputVat - inputVat,
            CashAndBankBalance = under?.Banks ?? 0, ReceivablesBalance = under?.Receivables ?? 0, PayablesBalance = under?.Payables ?? 0,
        };
    }

    public async Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime? asOf, string? module = null, CancellationToken ct = default)
    {
        var date = (asOf ?? SaudiTime.Today).Date;
        var end = date.AddDays(1);
        var moduleScope = ReportModuleScope.Normalize(module);
        var movements = from l in Lines
                        join e in Entries on l.JournalEntryId equals e.Id
                        where e.Date < end && (moduleScope == null || moduleScope.Contains(e.Module))
                        group l by l.AccountCode into g
                        select new { Code = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) };
        // صف لكل حساب له رصيد: هذا حجم القائمة نفسها
        var rows = await (from m in movements
                          join a in Accounts on m.Code equals a.Code
                          where m.Net != 0
                          orderby a.Code
                          select new { a.Code, a.NameAr, a.NameEn, a.Type, m.Net }).ToListAsync(ct);
        List<BalanceSheetLineDto> Section(AccountCategory type, int sign) => rows.Where(r => r.Type == type)
            .Select(r => new BalanceSheetLineDto { AccountCode = r.Code, NameAr = r.NameAr, NameEn = r.NameEn, Amount = sign * r.Net }).ToList();

        var sheet = new BalanceSheetDto
        {
            AsOf = date,
            Assets = Section(AccountCategory.Asset, 1), Liabilities = Section(AccountCategory.Liability, -1), Equity = Section(AccountCategory.Equity, -1),
            // إيراد دائن يزيد النتيجة ومصروف مدين ينقصها
            NetProfit = -rows.Where(r => r.Type is not (AccountCategory.Asset or AccountCategory.Liability or AccountCategory.Equity)).Sum(r => r.Net),
        };
        sheet.TotalAssets = sheet.Assets.Sum(l => l.Amount);
        sheet.TotalLiabilities = sheet.Liabilities.Sum(l => l.Amount);
        sheet.TotalEquity = sheet.Equity.Sum(l => l.Amount);
        sheet.TotalLiabilitiesAndEquity = sheet.TotalLiabilities + sheet.TotalEquity + sheet.NetProfit;
        sheet.IsBalanced = Math.Abs(sheet.TotalAssets - sheet.TotalLiabilitiesAndEquity) < 0.01m;
        return sheet;
    }

    /// <summary>
    /// ضريبة مدخلات مصاريف السيارات المرحّلة في الفترة (أسطر المصاريف الفعلية وسندات التكاليف الإضافية والمصروفات المدفوعة مقدماً):
    /// قيودها ليست فواتير ولا سندات فلا تلتقطها بقية الإقرار. السطر الملغى عُكس قيده فلا يدخل. ضريبة الاستيراد (المبلغ كله ضريبة)
    /// تدخل ضريبةً بلا وعاء هنا لأن وعاءها قيمة البضاعة في البيان الجمركي لا في السطر.
    /// </summary>
    private async Task<(decimal Net, decimal Vat)> CarChargesInputVatAsync(DateTime start, DateTime end, string[]? moduleScope, CancellationToken ct)
    {
        if (moduleScope != null && !moduleScope.Contains(PlatformModules.CarShowroom)) return (0, 0);
        var charges = await _db.Set<ERP.Core.Models.CarShowroom.AdditionalChargeLine>().AsNoTracking()
            .Where(l => l.Status == ERP.Core.Models.CarShowroom.ChargeStatus.Actual && l.JournalEntryId != null && l.IsVatRecoverable
                && l.Date >= start && l.Date < end && (l.VatAmount > 0 || l.IsInputTax))
            .Select(l => new { l.IsInputTax, l.BaseAmount, Vat = l.VatAmount * l.ExchangeRate }).ToListAsync(ct);
        var prepaid = await _db.Set<ERP.Core.Models.CarShowroom.PrepaidExpense>().AsNoTracking()
            .Where(e => e.Status != ERP.Core.Models.CarShowroom.PrepaidExpenseStatus.Cancelled && e.VatAmount > 0 && e.StartDate >= start && e.StartDate < end)
            .Select(e => new { e.TotalAmount, e.VatAmount }).ToListAsync(ct);
        var net = charges.Where(c => !c.IsInputTax).Sum(c => c.BaseAmount) + prepaid.Sum(e => e.TotalAmount);
        var vat = charges.Sum(c => c.IsInputTax ? c.BaseAmount : DocumentPricing.Round(c.Vat)) + prepaid.Sum(e => e.VatAmount);
        return (DocumentPricing.Round(net), DocumentPricing.Round(vat));
    }

    /// <summary>
    /// تسويات الضريبة بقيود يدوية في الفترة: ما قُيِّد يدوياً على حسابي ضريبة المخرجات والمدخلات.
    /// قيد سداد الضريبة للهيئة أو مقاصّتها ليس تسوية: يُعرف بأن سطوره الأخرى كلها نقدية/بنوك أو حسابات ضريبة، فيُستثنى.
    /// </summary>
    private async Task<(decimal Output, decimal Input)> ManualVatAdjustmentsAsync(DateTime start, DateTime end, string[]? moduleScope, CancellationToken ct)
    {
        var vatAccounts = new[] { DefaultAccounts.OutputVat, DefaultAccounts.InputVat };
        // القيد اليدوي وعكسه (العكس يُلغي أثر التسوية في فترته)
        var manualEntryIds = from e in Entries
                             where e.Date >= start && e.Date < end
                                   && (moduleScope == null || moduleScope.Contains(e.Module))
                                   && (e.SourceType == null || e.SourceType == "manual"
                                       || (e.SourceType == "reversal" && Entries.Any(o => o.Id == e.SourceReferenceId && (o.SourceType == null || o.SourceType == "manual"))))
                             select e.Id;
        // يُستثنى القيد الذي لا طرف مقابل له غير النقدية/البنوك: سداد للهيئة/استرداد منها أو مقاصّة بين الحسابين
        var totals = await Lines
            .Where(l => vatAccounts.Contains(l.AccountCode) && manualEntryIds.Contains(l.JournalEntryId)
                && Lines.Any(o => o.JournalEntryId == l.JournalEntryId && !vatAccounts.Contains(o.AccountCode) && !o.AccountCode.StartsWith(DefaultAccounts.Banks)))
            .GroupBy(l => l.AccountCode).Select(g => new { Code = g.Key, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) }).ToListAsync(ct);
        var output = totals.Where(t => t.Code == DefaultAccounts.OutputVat).Sum(t => t.Credit - t.Debit);
        var input = totals.Where(t => t.Code == DefaultAccounts.InputVat).Sum(t => t.Debit - t.Credit);
        return (DocumentPricing.Round(output), DocumentPricing.Round(input));
    }

    public async Task<VatReturnDto> GetVatReturnAsync(DateTime from, DateTime to, string? module = null, CancellationToken ct = default)
    {
        if (to < from) throw new ValidationFailedException(Messages.PeriodEndBeforeStart);
        // من سطور الفواتير المرحّلة في الفترة: كل سطر بتصنيفه الضريبي، محوَّلاً للعملة الأساسية، والمرتجعات تُطرح.
        // التجميع في قاعدة البيانات: صف لكل (مبيعات/مشتريات، تصنيف ضريبي، صادرات أو لا)
        var start = from.Date; var end = to.Date.AddDays(1);
        var moduleScope = ReportModuleScope.Normalize(module);
        var exportCodes = VatExemptionReasons.ExportCodes;
        var lines = await (from item in _db.Set<InvoiceItem>().AsNoTracking()
                           join i in _db.Set<Invoice>().AsNoTracking() on item.InvoiceId equals i.Id
                           where i.Status == "posted" && i.IssueDate >= start && i.IssueDate < end
                                 && (moduleScope == null || moduleScope.Contains(i.Module))
                           select new
                           {
                               Sales = i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn,
                               Return = i.Kind == InvoiceKind.SalesReturn || i.Kind == InvoiceKind.PurchaseReturn,
                               item.VatCategory, Export = exportCodes.Contains(item.VatExemptionReasonCode),
                               Net = item.TotalBeforeVat * (i.ExchangeRate <= 0 ? 1 : i.ExchangeRate),
                               Vat = item.VatAmount * (i.ExchangeRate <= 0 ? 1 : i.ExchangeRate),
                           } into l
                           group l by new { l.Sales, l.VatCategory, l.Export } into g
                           select new
                           {
                               g.Key.Sales, g.Key.VatCategory, g.Key.Export,
                               Net = g.Sum(l => l.Return ? -l.Net : l.Net), Vat = g.Sum(l => l.Return ? -l.Vat : l.Vat),
                           }).ToListAsync(ct);

        // exports: null = لا تمييز، true = الصادرات فقط، false = ما عداها
        decimal Sum(bool sales, VatCategory? category, bool vat, bool? exports = null)
            => DocumentPricing.Round(lines
                .Where(l => l.Sales == sales && (!category.HasValue || l.VatCategory == category) && (!exports.HasValue || l.Export == exports))
                .Sum(l => vat ? l.Vat : l.Net));

        // مصروفات وإيرادات مباشرة بضريبة مسجَّلة بسندات صرف/قبض (خاضعة للنسبة الأساسية)
        var vouchers = await _db.Set<Voucher>().AsNoTracking().Where(v => v.VatAmount > 0 && v.Date >= start && v.Date < end
                && (moduleScope == null || moduleScope.Contains(v.Module)))
            .GroupBy(v => v.Type).Select(g => new { Type = g.Key, Net = g.Sum(v => v.Amount - v.VatAmount), Vat = g.Sum(v => v.VatAmount) }).ToListAsync(ct);
        decimal VoucherNet(VoucherType type) => vouchers.Where(v => v.Type == type).Sum(v => v.Net);
        decimal VoucherVat(VoucherType type) => vouchers.Where(v => v.Type == type).Sum(v => v.Vat);

        var (outAdjustments, inAdjustments) = await ManualVatAdjustmentsAsync(start, end, moduleScope, ct);
        var (carChargesNet, carChargesVat) = await CarChargesInputVatAsync(start, end, moduleScope, ct);
        var outVat = Sum(true, null, vat: true) + VoucherVat(VoucherType.Receipt) + outAdjustments;
        var inVat = Sum(false, null, vat: true) + VoucherVat(VoucherType.Payment) + inAdjustments + carChargesVat;
        return new VatReturnDto
        {
            FromDate = from, ToDate = to,
            StandardRatedSales = Sum(true, VatCategory.Standard, false) + VoucherNet(VoucherType.Receipt), ZeroRatedSales = Sum(true, VatCategory.ZeroRated, false, exports: false),
            ExportSales = Sum(true, VatCategory.ZeroRated, false, exports: true),
            ExemptSales = Sum(true, VatCategory.Exempt, false), OutOfScopeSales = Sum(true, VatCategory.OutOfScope, false),
            OutputVat = outVat, OutputVatAdjustments = outAdjustments,
            StandardRatedPurchases = Sum(false, VatCategory.Standard, false) + VoucherNet(VoucherType.Payment) + carChargesNet, ZeroRatedPurchases = Sum(false, VatCategory.ZeroRated, false),
            ExemptPurchases = Sum(false, VatCategory.Exempt, false), OutOfScopePurchases = Sum(false, VatCategory.OutOfScope, false),
            InputVat = inVat, InputVatAdjustments = inAdjustments,
            NetVatPayable = outVat - inVat,
        };
    }

    public Task<LoadResult> LoadTrialBalanceAsync(DateTime? from, DateTime? to, DataSourceLoadOptions options, string? module = null, CancellationToken ct = default)
        => ReportLoader.LoadAsync(TrialBalanceQuery(from, to, module), options, nameof(TrialBalanceItemDto.AccountCode), ct,
            EntityLoader.Asc(nameof(TrialBalanceItemDto.AccountCode)));

    public async Task<LoadResult> LoadAccountStatementEntriesAsync(string accountCode, DateTime? from, DateTime? to, DataSourceLoadOptions options, string? module = null, CancellationToken ct = default)
        => await ReportLoader.LoadAsync(StatementEntries(await AccountAsync(accountCode, ct), from, to, module), options, nameof(AccountLedgerEntryDto.Id), ct,
            EntityLoader.Asc(nameof(AccountLedgerEntryDto.Date)), EntityLoader.Asc(nameof(AccountLedgerEntryDto.EntryNumber)));

    public Task<LoadResult> LoadAccountBalancesAsync(DataSourceLoadOptions options, string? module = null, CancellationToken ct = default)
    {
        var moduleScope = ReportModuleScope.Normalize(module);
        IQueryable<AccountBalanceRowDto> rows;
        if (moduleScope == null)
        {
            rows = Accounts.Select(a => new AccountBalanceRowDto
            {
                Id = a.Id, Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, Type = a.Type, Level = a.Level,
                Balance = a.Balance, IsDebitNature = a.IsDebitNature,
                // الرصيد بإشارته: مدين الطبيعة السالب (بنك مكشوف) يظهر دائناً، ودائن الطبيعة السالب يظهر مديناً
                Debit = a.IsDebitNature ? (a.Balance > 0 ? a.Balance : 0m) : (a.Balance < 0 ? -a.Balance : 0m),
                Credit = a.IsDebitNature ? (a.Balance < 0 ? -a.Balance : 0m) : (a.Balance > 0 ? a.Balance : 0m),
            });
        }
        else
        {
            var totals = from line in Lines
                         join entry in Entries on line.JournalEntryId equals entry.Id
                         where moduleScope.Contains(entry.Module)
                         group line by line.AccountCode into grouped
                         select new { Code = grouped.Key, Debit = grouped.Sum(x => x.Debit), Credit = grouped.Sum(x => x.Credit) };
            rows = from total in totals
                   join account in Accounts on total.Code equals account.Code
                   let balance = account.IsDebitNature ? total.Debit - total.Credit : total.Credit - total.Debit
                   select new AccountBalanceRowDto
                   {
                       Id = account.Id, Code = account.Code, NameAr = account.NameAr, NameEn = account.NameEn, Type = account.Type, Level = account.Level,
                       Balance = balance, IsDebitNature = account.IsDebitNature,
                       Debit = account.IsDebitNature ? (balance > 0 ? balance : 0m) : (balance < 0 ? -balance : 0m),
                       Credit = account.IsDebitNature ? (balance < 0 ? -balance : 0m) : (balance > 0 ? balance : 0m),
                   };
        }
        return EntityLoader.LoadRowsAsync(rows, options, nameof(AccountBalanceRowDto.Id), ct, EntityLoader.Asc(nameof(AccountBalanceRowDto.Code)));
    }

    public Task<LoadResult> LoadJournalLedgerAsync(DataSourceLoadOptions options, string? module = null, CancellationToken ct = default)
    {
        var moduleScope = ReportModuleScope.Normalize(module);
        return EntityLoader.LoadRowsAsync(
            from l in _db.Set<JournalEntryLine>().AsNoTracking()
            join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
            where moduleScope == null || moduleScope.Contains(e.Module)
            select new JournalLedgerRowDto
            {
                LineId = l.Id, EntryId = e.Id, EntryNumber = e.EntryNumber, Date = e.Date, Description = e.Description,
                AccountCode = l.AccountCode, AccountName = l.AccountName, Debit = l.Debit, Credit = l.Credit, ReferenceType = e.ReferenceType,
            }, options, nameof(JournalLedgerRowDto.LineId), ct,
            EntityLoader.Desc(nameof(JournalLedgerRowDto.Date)), EntityLoader.Desc(nameof(JournalLedgerRowDto.EntryNumber)));
    }
}
