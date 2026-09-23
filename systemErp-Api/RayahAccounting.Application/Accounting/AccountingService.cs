using Microsoft.EntityFrameworkCore;
using RayahAccounting.Application.Interfaces;
using RayahAccounting.Domain.Entities;
using RayahAccounting.Domain.Enums;

namespace RayahAccounting.Application.Accounting;

public class AccountingService : IAccountingService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantService _tenantService;

    public AccountingService(IApplicationDbContext db, ITenantService tenantService)
    {
        _db = db;
        _tenantService = tenantService;
    }

    public async Task<JournalEntry> CreateJournalEntryAsync(CreateJournalEntryCommand command, CancellationToken ct = default)
    {
        var totalDebit = command.Lines.Sum(l => l.Debit);
        var totalCredit = command.Lines.Sum(l => l.Credit);

        if (Math.Abs(totalDebit - totalCredit) > 0.001m)
        {
            throw new InvalidOperationException($"القيد غير متوازن! مجموع المدين ({totalDebit:N2}) لا يساوي مجموع الدائن ({totalCredit:N2})");
        }

        var entryNumber = $"JV-{DateTime.UtcNow:yyyyMM}-{DateTime.UtcNow.Ticks % 10000:D4}";

        var entry = new JournalEntry
        {
            TenantId = _tenantService.CurrentTenantId,
            EntryNumber = entryNumber,
            EntryDate = command.EntryDate,
            DescriptionAr = command.DescriptionAr,
            ReferenceType = command.ReferenceType,
            ReferenceId = command.ReferenceId,
            ReferenceNumber = command.ReferenceNumber,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            Status = JournalEntryStatus.Posted
        };

        foreach (var lineDto in command.Lines)
        {
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == lineDto.AccountCode, ct);
            entry.Lines.Add(new JournalEntryLine
            {
                TenantId = _tenantService.CurrentTenantId,
                JournalEntryId = entry.Id,
                AccountId = account?.Id ?? Guid.NewGuid().ToString("N"),
                AccountCode = lineDto.AccountCode,
                AccountNameAr = lineDto.AccountNameAr,
                Description = lineDto.Description,
                Debit = lineDto.Debit,
                Credit = lineDto.Credit,
                CostCenter = lineDto.CostCenter
            });

            // Update account balance
            if (account != null)
            {
                account.DebitBalance += lineDto.Debit;
                account.CreditBalance += lineDto.Credit;
            }
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return entry;
    }

    public async Task<List<TrialBalanceItemDto>> GetTrialBalanceAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        var accounts = await _db.Accounts.OrderBy(a => a.Code).ToListAsync(ct);
        var result = new List<TrialBalanceItemDto>();

        foreach (var acc in accounts)
        {
            decimal closingDebit = 0m;
            decimal closingCredit = 0m;

            if (acc.Category == AccountCategory.Assets || acc.Category == AccountCategory.Expenses)
            {
                var net = acc.DebitBalance - acc.CreditBalance;
                if (net >= 0) closingDebit = net;
                else closingCredit = -net;
            }
            else
            {
                var net = acc.CreditBalance - acc.DebitBalance;
                if (net >= 0) closingCredit = net;
                else closingDebit = -net;
            }

            result.Add(new TrialBalanceItemDto(
                acc.Code,
                acc.NameAr,
                acc.Category.ToString(),
                0m,
                0m,
                acc.DebitBalance,
                acc.CreditBalance,
                closingDebit,
                closingCredit
            ));
        }

        return result;
    }

    public async Task<FinancialReportSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default)
    {
        var accounts = await _db.Accounts.ToListAsync(ct);

        var totalAssets = accounts.Where(a => a.Category == AccountCategory.Assets).Sum(a => a.CurrentBalance);
        var totalLiabilities = accounts.Where(a => a.Category == AccountCategory.Liabilities).Sum(a => a.CurrentBalance);
        var totalEquity = accounts.Where(a => a.Category == AccountCategory.Equity).Sum(a => a.CurrentBalance);
        var totalRevenues = accounts.Where(a => a.Category == AccountCategory.Revenues).Sum(a => a.CurrentBalance);
        var totalExpenses = accounts.Where(a => a.Category == AccountCategory.Expenses).Sum(a => a.CurrentBalance);

        var netProfit = totalRevenues - totalExpenses;
        var balanced = Math.Abs(totalAssets - (totalLiabilities + totalEquity + netProfit)) < 1.0m;

        return new FinancialReportSummaryDto(
            $"Q{((DateTime.UtcNow.Month - 1) / 3) + 1}-{DateTime.UtcNow.Year}",
            DateTime.UtcNow,
            totalAssets,
            totalLiabilities,
            totalEquity,
            totalRevenues,
            totalExpenses,
            netProfit,
            balanced
        );
    }

    public async Task<List<AccountLedgerEntryDto>> GetAccountStatementAsync(string accountCode, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        var lines = await _db.JournalEntryLines
            .Include(l => l.JournalEntryId)
            .Where(l => l.AccountCode == accountCode)
            .OrderBy(l => l.CreatedAtUtc)
            .ToListAsync(ct);

        var ledger = new List<AccountLedgerEntryDto>();
        decimal runningBalance = 0m;

        foreach (var l in lines)
        {
            runningBalance += (l.Debit - l.Credit);
            ledger.Add(new AccountLedgerEntryDto(
                l.CreatedAtUtc,
                l.JournalEntryId,
                l.Description,
                l.Debit,
                l.Credit,
                runningBalance,
                l.CostCenter
            ));
        }

        return ledger;
    }

    public async Task PostAutomaticInvoiceJournalAsync(Invoice invoice, CancellationToken ct = default)
    {
        var lines = new List<JournalEntryLineDto>();

        if (invoice.Type == InvoiceType.PurchaseInvoice)
        {
            // فاتورة مشتريات:
            // من حـ/ المشتريات (أو المخزون)
            // من حـ/ ضريبة القيمة المضافة المدخلة
            // إلى حـ/ المورد أو الصندوق/البنك
            lines.Add(new JournalEntryLineDto("1141", "مخزون البضاعة", $"مشتريات فاتورة {invoice.InvoiceNumber}", invoice.Subtotal, 0));
            lines.Add(new JournalEntryLineDto("1131", "ضريبة القيمة المضافة المدخلة (مشتريات)", $"ضريبة 15% فاتورة {invoice.InvoiceNumber}", invoice.VatTotal, 0));
            lines.Add(new JournalEntryLineDto("2111", "الموردون والدائنون", $"استحقاق فاتورة {invoice.InvoiceNumber} - {invoice.PartyName}", 0, invoice.GrandTotal));
        }
        else
        {
            // فاتورة مبيعات:
            // من حـ/ العميل أو الصندوق/البنك
            // إلى حـ/ المبيعات
            // إلى حـ/ ضريبة القيمة المضافة المخرجة
            lines.Add(new JournalEntryLineDto("1121", "العملاء والمدينون", $"مبيعات فاتورة {invoice.InvoiceNumber} - {invoice.PartyName}", invoice.GrandTotal, 0));
            lines.Add(new JournalEntryLineDto("4101", "إيرادات المبيعات", $"صافي مبيعات فاتورة {invoice.InvoiceNumber}", 0, invoice.Subtotal));
            lines.Add(new JournalEntryLineDto("2131", "ضريبة القيمة المضافة المخرجة (مبيعات)", $"ضريبة 15% فاتورة {invoice.InvoiceNumber}", 0, invoice.VatTotal));

            // قيد تكلفة المبيعات (أثر البيع على المخزون وتكلفة الإيرادات):
            decimal costTotal = invoice.TotalCost > 0 ? invoice.TotalCost : (invoice.Items != null ? invoice.Items.Sum(i => i.Quantity * i.UnitCost) : 0);
            if (costTotal > 0)
            {
                lines.Add(new JournalEntryLineDto("5101", "تكلفة المبيعات", $"تكلفة مبيعات فاتورة {invoice.InvoiceNumber}", costTotal, 0));
                lines.Add(new JournalEntryLineDto("1141", "مخزون البضاعة", $"تكلفة مبيعات فاتورة {invoice.InvoiceNumber}", 0, costTotal));
            }
        }

        var command = new CreateJournalEntryCommand(
            invoice.IssueDate,
            $"قيد آلي ناتج عن فاتورة {invoice.InvoiceNumber}",
            "invoice",
            invoice.Id,
            invoice.InvoiceNumber,
            lines
        );

        await CreateJournalEntryAsync(command, ct);
    }

    public async Task PostAutomaticVoucherJournalAsync(Voucher voucher, CancellationToken ct = default)
    {
        var lines = new List<JournalEntryLineDto>();

        if (voucher.Type == VoucherType.Receipt)
        {
            // سند قبض:
            // من حـ/ الصندوق أو البنك
            // إلى حـ/ العميل أو الطرف المستلم منه
            lines.Add(new JournalEntryLineDto("1111", "الصندوق الرئيسي / البنك", $"تحصيل سند قبض {voucher.VoucherNumber}", voucher.Amount, 0));
            lines.Add(new JournalEntryLineDto("1121", "العملاء والمدينون", $"سداد عميل - {voucher.PartyName}", 0, voucher.Amount));
        }
        else
        {
            // سند صرف:
            // من حـ/ المورد أو المصروف
            // إلى حـ/ الصندوق أو البنك
            lines.Add(new JournalEntryLineDto("2111", "الموردون والدائنون", $"صرف مستحقات - {voucher.PartyName}", voucher.Amount, 0));
            lines.Add(new JournalEntryLineDto("1111", "الصندوق الرئيسي / البنك", $"سداد سند صرف {voucher.VoucherNumber}", 0, voucher.Amount));
        }

        var command = new CreateJournalEntryCommand(
            voucher.Date,
            $"قيد آلي ناتج عن سند {(voucher.Type == VoucherType.Receipt ? "قبض" : "صرف")} {voucher.VoucherNumber}",
            "voucher",
            voucher.Id,
            voucher.VoucherNumber,
            lines
        );

        await CreateJournalEntryAsync(command, ct);
    }
}
