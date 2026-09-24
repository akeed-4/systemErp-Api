namespace Erp.Modules.Accounting.Contracts;

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Revenue,
    Expense,
}

/// <summary>
/// What an account is used for. Modules post by purpose; each tenant maps purposes to accounts
/// (accounting.PostingAccountMappings), so no module ever hard-codes an account code (§13.3).
/// </summary>
public enum PostingPurpose
{
    CashOnHand,
    CardClearing,
    BankControl,
    CustomerControl,
    SupplierControl,
    CustomerAdvances,
    CustomerCredit,
    OutputVat,
    InputVat,
    Inventory,
    VehicleInventory,
    GoodsReceivedNotInvoiced,
    SalesRevenue,
    SalesReturns,
    SalesDiscount,
    PosRevenue,
    CarSalesRevenueNew,
    UsedCarMarginRevenue,
    ContractRevenue,
    CostOfGoodsSold,
    CostOfVehiclesSold,
    InventoryAdjustment,
    PurchasePriceVariance,
    CashOverShort,
    RetentionReceivable,
    FinancingBankReceivable,
    FinancingBankPayable,
    LoyaltyLiability,
    FixedAssets,
    AccumulatedDepreciation,
    DepreciationExpense,
    GeneralExpenses,
    OpeningBalances,
}

public enum PartyType
{
    Customer,
    Supplier,
    BankAccount,
    Other,
}

/// <summary>The business document that produced a journal entry.</summary>
public sealed record SourceRef(string Module, string DocumentType, Guid DocumentId, string DocumentNumber);

/// <summary>An account named either by purpose (resolved through the tenant's mapping) or by id (e.g. a customer's own sub-account).</summary>
public sealed record AccountRef
{
    private AccountRef(Guid? accountId, PostingPurpose? purpose)
    {
        AccountId = accountId;
        Purpose = purpose;
    }

    public Guid? AccountId { get; }

    public PostingPurpose? Purpose { get; }

    public static AccountRef ById(Guid accountId) => new(accountId, null);

    public static AccountRef For(PostingPurpose purpose) => new(null, purpose);
}

public sealed record PartyRef(PartyType Type, Guid Id);

public sealed record PostingLine(
    AccountRef Account,
    decimal Debit,
    decimal Credit,
    Guid? CostCenterId = null,
    PartyRef? Party = null,
    string? Notes = null);

/// <param name="Kind">Distinguishes several postings of one document (e.g. "invoice", "cogs", "payment").</param>
public sealed record PostingRequest(
    SourceRef Source,
    string Kind,
    DateOnly Date,
    string Description,
    IReadOnlyList<PostingLine> Lines);

public sealed record PostingResult(Guid JournalEntryId, string EntryNumber, bool AlreadyPosted);

/// <summary>
/// The only way modules create journal entries. Runs in the caller's unit of work, so the business document and its
/// entry commit together. Idempotent per (source, kind): posting the same source twice returns the existing entry.
/// Rejects unbalanced, empty, zero or non-postable-account entries and dates in closed periods.
/// </summary>
public interface IAccountingPostingService
{
    Task<PostingResult> PostAsync(PostingRequest request, CancellationToken cancellationToken);

    /// <summary>Posts the mirror entry of the posted entry for (source, kind) and marks the original reversed.</summary>
    Task<PostingResult> ReverseAsync(SourceRef source, string kind, DateOnly date, string reason, CancellationToken cancellationToken);

    /// <summary>
    /// For documents whose financial effect changes after posting (e.g. an opening balance): reverses the live posting of
    /// (source, kind) if any, then posts <paramref name="request"/> unless it has no lines. Returns null when nothing is posted.
    /// </summary>
    Task<PostingResult?> RepostAsync(PostingRequest request, CancellationToken cancellationToken);
}

/// <summary>Builds the opening-balance entry of a party/bank sub-account: the account against OpeningBalances (account 33).</summary>
public static class OpeningBalancePosting
{
    public const string Kind = "opening_balance";

    /// <param name="debitNature">True for customers and bank accounts (asset), false for suppliers (liability).</param>
    public static PostingRequest Build(SourceRef source, DateOnly date, Guid accountId, decimal amount, bool debitNature, string description)
    {
        if (amount == 0)
        {
            return new PostingRequest(source, Kind, date, description, []);
        }

        var value = Math.Abs(amount);
        var accountOnDebit = debitNature == amount > 0;
        return new PostingRequest(source, Kind, date, description,
        [
            new PostingLine(AccountRef.ById(accountId), accountOnDebit ? value : 0, accountOnDebit ? 0 : value),
            new PostingLine(AccountRef.For(PostingPurpose.OpeningBalances), accountOnDebit ? 0 : value, accountOnDebit ? value : 0),
        ]);
    }
}

public sealed record AccountSummary(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    AccountType Type,
    bool IsPostable,
    bool IsActive,
    string? LinkedEntityType,
    Guid? LinkedEntityId);

public interface IAccountLookup
{
    Task<AccountSummary?> FindAsync(Guid accountId, CancellationToken cancellationToken);

    Task<AccountSummary?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, AccountSummary>> FindManyAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    Task<AccountSummary> ResolveAsync(PostingPurpose purpose, CancellationToken cancellationToken);
}

/// <param name="LinkedEntityType">customer | supplier | bank.</param>
public sealed record SubAccountRequest(
    PostingPurpose ParentPurpose,
    string NameAr,
    string NameEn,
    string LinkedEntityType,
    Guid LinkedEntityId,
    string? PreferredCode = null,
    string? CurrencyCode = null);

/// <summary>Creates the per-party GL sub-accounts (customers under 112, suppliers under 211, bank accounts under 111).</summary>
public interface IAccountProvisioningService
{
    Task<AccountSummary> CreateSubAccountAsync(SubAccountRequest request, CancellationToken cancellationToken);

    Task RenameAsync(Guid accountId, string nameAr, string nameEn, CancellationToken cancellationToken);
}

/// <summary>Balances in the account's natural sign (debit-nature: debit − credit; credit-nature: credit − debit).</summary>
public interface IAccountBalanceQueries
{
    Task<decimal> GetBalanceAsync(Guid accountId, DateOnly? asOf, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, decimal>> GetBalancesAsync(IReadOnlyCollection<Guid> accountIds, DateOnly? asOf, CancellationToken cancellationToken);
}

public sealed record AccountStatementEntry(DateOnly Date, string? EntryNumber, string Description, string? Reference, decimal Debit, decimal Credit, decimal Balance);

public sealed record AccountStatement(
    Guid AccountId,
    string AccountCode,
    string AccountNameAr,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<AccountStatementEntry> Entries,
    decimal ClosingBalance);

/// <summary>Running-balance statement of one account (and its sub-accounts), used for customer/supplier/bank statements.</summary>
public interface IAccountStatementService
{
    Task<AccountStatement> GetAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

public interface ICostCenterLookup
{
    Task<bool> ExistsAsync(Guid costCenterId, CancellationToken cancellationToken);
}
