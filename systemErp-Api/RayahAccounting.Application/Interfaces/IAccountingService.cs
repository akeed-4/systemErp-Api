using RayahAccounting.Domain.Entities;

namespace RayahAccounting.Application.Interfaces;

public record JournalEntryLineDto(
    string AccountCode,
    string AccountNameAr,
    string Description,
    decimal Debit,
    decimal Credit,
    string? CostCenter = null
);

public record CreateJournalEntryCommand(
    DateTime EntryDate,
    string DescriptionAr,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceNumber,
    List<JournalEntryLineDto> Lines
);

public record TrialBalanceItemDto(
    string AccountCode,
    string AccountNameAr,
    string AccountCategory,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit
);

public record FinancialReportSummaryDto(
    string Period,
    DateTime GeneratedAtUtc,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    decimal TotalRevenues,
    decimal TotalExpenses,
    decimal NetProfitOrLoss,
    bool IsBalanceSheetBalanced
);

public record AccountLedgerEntryDto(
    DateTime Date,
    string EntryNumber,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal RunningBalance,
    string? ReferenceNumber
);

public interface IAccountingService
{
    Task<JournalEntry> CreateJournalEntryAsync(CreateJournalEntryCommand command, CancellationToken ct = default);
    Task<List<TrialBalanceItemDto>> GetTrialBalanceAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
    Task<FinancialReportSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default);
    Task<List<AccountLedgerEntryDto>> GetAccountStatementAsync(string accountCode, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
    Task PostAutomaticInvoiceJournalAsync(Invoice invoice, CancellationToken ct = default);
    Task PostAutomaticVoucherJournalAsync(Voucher voucher, CancellationToken ct = default);
}
