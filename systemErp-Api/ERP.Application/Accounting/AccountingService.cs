using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Application.Accounting;

/// <summary>
/// محرك المحاسبة المركزي - المصدر الوحيد لإنشاء وترحيل القيود اليومية في النظام.
/// كل وحدة (مبيعات، مشتريات، نقاط بيع، معارض سيارات) يجب أن تستخدم هذه الخدمة
/// بدلاً من إنشاء قيود مباشرة، حتى يبقى الترحيل المحاسبي متسقاً في كل مكان.
/// </summary>
public class AccountingService : IAccountingService
{
    // TODO(Phase 2): استبدال هذه الشيفرات الثابتة بمزوّد إعدادات (IDefaultAccountsProvider)
    // يقرأ حسابات افتراضية مهيّأة لكل مستأجر بدلاً من قيم مبرمجة، تماشياً مع القرار المعماري رقم 6.
    private const string DefaultReceivableAccountCode = "112";
    private const string DefaultPayableAccountCode = "211";
    private const string DefaultOutputVatAccountCode = "213";
    private const string DefaultInputVatAccountCode = "1131";
    private const string DefaultRevenueAccountCode = "411";
    private const string DefaultInventoryAccountCode = "1141";
    private const string DefaultCogsAccountCode = "511";

    private readonly IApplicationDbContext _db;
    private readonly IInventoryService _inventoryService;

    public AccountingService(IApplicationDbContext db, IInventoryService inventoryService)
    {
        _db = db;
        _inventoryService = inventoryService;
    }

    public async Task<JournalEntry> CreateJournalEntryAsync(CreateJournalEntryCommand command, CancellationToken ct = default)
    {
        var totalDebit = command.Lines.Sum(l => l.Debit);
        var totalCredit = command.Lines.Sum(l => l.Credit);

        if (Math.Abs(totalDebit - totalCredit) > 0.01m)
        {
            throw new InvalidOperationException(
                $"القيد غير متوازن! مجموع المدين ({totalDebit:N2}) لا يساوي مجموع الدائن ({totalCredit:N2})");
        }

        var entry = new JournalEntry
        {
            EntryNumber = $"JV-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Date = command.EntryDate,
            Description = command.Description,
            ReferenceType = command.ReferenceType,
            ReferenceId = command.ReferenceId,
            ReferenceNumber = command.ReferenceNumber,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            Status = JournalEntryStatus.Posted,
        };

        foreach (var lineDto in command.Lines)
        {
            entry.Lines.Add(new JournalEntryLine
            {
                AccountCode = lineDto.AccountCode,
                AccountName = lineDto.AccountName,
                Debit = lineDto.Debit,
                Credit = lineDto.Credit,
                Notes = lineDto.Notes,
            });

            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == lineDto.AccountCode, ct);
            if (account != null)
            {
                account.Balance += account.IsDebitNature
                    ? lineDto.Debit - lineDto.Credit
                    : lineDto.Credit - lineDto.Debit;
            }
        }

        _db.JournalEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        return entry;
    }

    public async Task<JournalEntry> ReverseJournalEntryAsync(Guid journalEntryId, CancellationToken ct = default)
    {
        var original = await _db.JournalEntries.Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == journalEntryId, ct);
        if (original == null)
        {
            throw new KeyNotFoundException("القيد المطلوب عكسه غير موجود");
        }
        if (original.Status == JournalEntryStatus.Reversed)
        {
            throw new InvalidOperationException("تم عكس هذا القيد مسبقاً");
        }

        var reversalLines = original.Lines
            .Select(l => new JournalEntryLineDto(l.AccountCode, l.AccountName, l.Credit, l.Debit, $"عكس قيد رقم {original.EntryNumber}"))
            .ToList();

        var reversalCommand = new CreateJournalEntryCommand(
            DateTime.UtcNow,
            $"قيد عكسي للقيد رقم {original.EntryNumber}",
            original.ReferenceType,
            original.ReferenceId,
            original.ReferenceNumber,
            reversalLines);

        var reversalEntry = await CreateJournalEntryAsync(reversalCommand, ct);

        original.Status = JournalEntryStatus.Reversed;
        await _db.SaveChangesAsync(ct);

        return reversalEntry;
    }

    public async Task<List<TrialBalanceItemDto>> GetTrialBalanceAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        var accounts = await _db.Accounts.OrderBy(a => a.Code).ToListAsync(ct);

        return accounts.Select(a =>
        {
            var closingDebit = a.IsDebitNature && a.Balance > 0 ? a.Balance : (!a.IsDebitNature && a.Balance < 0 ? -a.Balance : 0m);
            var closingCredit = !a.IsDebitNature && a.Balance > 0 ? a.Balance : (a.IsDebitNature && a.Balance < 0 ? -a.Balance : 0m);

            return new TrialBalanceItemDto(
                a.Code,
                a.NameAr,
                a.Type.ToString(),
                0m, 0m,
                closingDebit, closingCredit,
                closingDebit, closingCredit
            );
        }).ToList();
    }

    public async Task<FinancialReportSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default)
    {
        var accounts = await _db.Accounts.ToListAsync(ct);

        decimal SumOf(AccountCategory category) => accounts.Where(a => a.Type == category).Sum(a => a.Balance);

        var totalAssets = SumOf(AccountCategory.Asset);
        var totalLiabilities = SumOf(AccountCategory.Liability);
        var totalEquity = SumOf(AccountCategory.Equity);
        var totalRevenues = SumOf(AccountCategory.Revenue);
        var totalExpenses = SumOf(AccountCategory.Expense);
        var netProfit = totalRevenues - totalExpenses;

        return new FinancialReportSummaryDto(
            $"Q{((DateTime.UtcNow.Month - 1) / 3) + 1}-{DateTime.UtcNow.Year}",
            DateTime.UtcNow,
            totalAssets,
            totalLiabilities,
            totalEquity,
            totalRevenues,
            totalExpenses,
            netProfit,
            Math.Abs(totalAssets - (totalLiabilities + totalEquity + netProfit)) < 1.0m
        );
    }

    public async Task<List<AccountLedgerEntryDto>> GetAccountStatementAsync(string accountCode, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        var linesQuery = _db.JournalEntryLines
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountCode == accountCode);

        if (fromDate.HasValue) linesQuery = linesQuery.Where(l => l.JournalEntry.Date >= fromDate.Value);
        if (toDate.HasValue) linesQuery = linesQuery.Where(l => l.JournalEntry.Date <= toDate.Value);

        var lines = await linesQuery.OrderBy(l => l.JournalEntry.Date).ToListAsync(ct);

        decimal runningBalance = 0m;
        var ledger = new List<AccountLedgerEntryDto>();
        foreach (var l in lines)
        {
            runningBalance += l.Debit - l.Credit;
            ledger.Add(new AccountLedgerEntryDto(
                l.JournalEntry.Date,
                l.JournalEntry.EntryNumber,
                l.Notes ?? l.JournalEntry.Description,
                l.Debit,
                l.Credit,
                runningBalance,
                l.JournalEntry.ReferenceNumber
            ));
        }

        return ledger;
    }

    public async Task PostAutomaticInvoiceJournalAsync(Invoice invoice, CancellationToken ct = default)
    {
        var lines = new List<JournalEntryLineDto>();
        var isPurchase = invoice.InvoiceType == InvoiceType.PurchaseInvoice;

        if (isPurchase)
        {
            lines.Add(new JournalEntryLineDto(DefaultInventoryAccountCode, "المخزون", invoice.Subtotal, 0, $"مشتريات فاتورة {invoice.InvoiceNumber}"));
            if (invoice.VatTotal > 0)
                lines.Add(new JournalEntryLineDto(DefaultInputVatAccountCode, "ضريبة مدخلات", invoice.VatTotal, 0, $"ضريبة مشتريات {invoice.InvoiceNumber}"));
            lines.Add(new JournalEntryLineDto(DefaultPayableAccountCode, "الموردون والدائنون", 0, invoice.GrandTotal, $"استحقاق فاتورة {invoice.InvoiceNumber} - {invoice.PartyName}"));

            foreach (var item in invoice.Items)
            {
                await _inventoryService.RecordMovementAsync(
                    new StockMovementDto(item.ItemId.ToString(), StockMovementType.InPurchase, item.Quantity, item.UnitPrice, invoice.InvoiceNumber), ct);
            }
        }
        else
        {
            lines.Add(new JournalEntryLineDto(DefaultReceivableAccountCode, "العملاء والمدينون", invoice.GrandTotal, 0, $"مبيعات فاتورة {invoice.InvoiceNumber} - {invoice.PartyName}"));
            lines.Add(new JournalEntryLineDto(DefaultRevenueAccountCode, "إيرادات المبيعات", 0, invoice.Subtotal, $"صافي مبيعات فاتورة {invoice.InvoiceNumber}"));
            if (invoice.VatTotal > 0)
                lines.Add(new JournalEntryLineDto(DefaultOutputVatAccountCode, "ضريبة مخرجات", 0, invoice.VatTotal, $"ضريبة مبيعات {invoice.InvoiceNumber}"));

            var costTotal = invoice.TotalCost > 0 ? invoice.TotalCost : invoice.Items.Sum(i => i.Quantity * i.UnitCost);
            if (costTotal > 0)
            {
                lines.Add(new JournalEntryLineDto(DefaultCogsAccountCode, "تكلفة المبيعات", costTotal, 0, $"تكلفة مبيعات فاتورة {invoice.InvoiceNumber}"));
                lines.Add(new JournalEntryLineDto(DefaultInventoryAccountCode, "المخزون", 0, costTotal, $"تكلفة مبيعات فاتورة {invoice.InvoiceNumber}"));
            }

            foreach (var item in invoice.Items)
            {
                await _inventoryService.RecordMovementAsync(
                    new StockMovementDto(item.ItemId.ToString(), StockMovementType.OutSales, item.Quantity, item.UnitCost, invoice.InvoiceNumber), ct);
            }
        }

        var command = new CreateJournalEntryCommand(
            invoice.IssueDate,
            $"قيد آلي ناتج عن فاتورة {invoice.InvoiceNumber}",
            "Invoice",
            invoice.Id,
            invoice.InvoiceNumber,
            lines);

        var entry = await CreateJournalEntryAsync(command, ct);

        invoice.JournalEntryId = entry.Id;
        invoice.Status = "Posted";
        await _db.SaveChangesAsync(ct);
    }

    public async Task PostAutomaticVoucherJournalAsync(Voucher voucher, CancellationToken ct = default)
    {
        var lines = new List<JournalEntryLineDto>();

        if (voucher.Type == VoucherType.Receipt)
        {
            lines.Add(new JournalEntryLineDto(voucher.TreasuryAccountCode, "الصندوق/البنك", voucher.Amount, 0, $"سند قبض {voucher.VoucherNumber}"));
            lines.Add(new JournalEntryLineDto(voucher.PartyAccountCode, "حساب الطرف", 0, voucher.Amount, $"سند قبض {voucher.VoucherNumber} - {voucher.PartyName}"));
        }
        else
        {
            lines.Add(new JournalEntryLineDto(voucher.PartyAccountCode, "حساب الطرف", voucher.Amount, 0, $"سند صرف {voucher.VoucherNumber} - {voucher.PartyName}"));
            lines.Add(new JournalEntryLineDto(voucher.TreasuryAccountCode, "الصندوق/البنك", 0, voucher.Amount, $"سند صرف {voucher.VoucherNumber}"));
        }

        var command = new CreateJournalEntryCommand(
            voucher.Date,
            $"قيد آلي ناتج عن سند {(voucher.Type == VoucherType.Receipt ? "قبض" : "صرف")} {voucher.VoucherNumber}",
            "Voucher",
            voucher.Id,
            voucher.VoucherNumber,
            lines);

        var entry = await CreateJournalEntryAsync(command, ct);

        voucher.JournalEntryId = entry.Id;
        await _db.SaveChangesAsync(ct);
    }
}
