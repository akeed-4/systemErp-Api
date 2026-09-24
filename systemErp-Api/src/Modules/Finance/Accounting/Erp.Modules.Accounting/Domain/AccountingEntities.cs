using Erp.Modules.Accounting.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Errors;

namespace Erp.Modules.Accounting.Domain;

internal sealed class Account : TenantEntity
{
    private Account()
    {
    }

    public Account(string code, string nameAr, string nameEn, AccountType type, Account? parent, bool isDebitNature, bool isSystem)
    {
        Code = code.Trim();
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        Type = type;
        ParentId = parent?.Id;
        Level = (parent?.Level ?? 0) + 1;
        IsDebitNature = isDebitNature;
        IsSystem = isSystem;
        IsPostable = true;
        IsActive = true;
        parent?.MarkAsParent();
    }

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public AccountType Type { get; private set; }

    public Guid? ParentId { get; private set; }

    public int Level { get; private set; }

    public bool IsDebitNature { get; private set; }

    public bool IsSystem { get; private set; }

    /// <summary>Only leaf accounts take postings.</summary>
    public bool IsPostable { get; private set; }

    public bool IsActive { get; private set; }

    public string? CurrencyCode { get; private set; }

    /// <summary>general | customer | supplier | bank.</summary>
    public string? LinkedEntityType { get; private set; }

    public Guid? LinkedEntityId { get; private set; }

    public string? Notes { get; private set; }

    public void MarkAsParent() => IsPostable = false;

    public void LinkTo(string entityType, Guid entityId)
    {
        LinkedEntityType = entityType;
        LinkedEntityId = entityId;
    }

    public void Update(string nameAr, string nameEn, string? notes, string? currencyCode, bool isActive)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        Notes = notes;
        CurrencyCode = currencyCode;
        if (!isActive && IsSystem)
        {
            throw ErpException.Conflict("system_account", "System accounts cannot be deactivated.", "لا يمكن إيقاف حساب نظامي.");
        }

        IsActive = isActive;
    }

    public void Rename(string nameAr, string nameEn)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
    }

    public decimal NaturalBalance(decimal debit, decimal credit) => IsDebitNature ? debit - credit : credit - debit;
}

internal sealed class CostCenter : TenantEntity
{
    private CostCenter()
    {
    }

    public CostCenter(string code, string nameAr, string nameEn, string? description)
    {
        Code = code.Trim().ToUpperInvariant();
        Update(nameAr, nameEn, description, true);
    }

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string nameAr, string nameEn, string? description, bool isActive)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        Description = description;
        IsActive = isActive;
    }
}

/// <summary>A calendar month of the tenant's books. Posting into a closed period is rejected.</summary>
internal sealed class FiscalPeriod : TenantEntity
{
    private FiscalPeriod()
    {
    }

    public FiscalPeriod(int year, int month)
    {
        Year = year;
        Month = month;
        StartDate = new DateOnly(year, month, 1);
        EndDate = StartDate.AddMonths(1).AddDays(-1);
    }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public bool IsClosed { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Guid? ClosedBy { get; private set; }

    public void Close(DateTimeOffset at, Guid? by)
    {
        IsClosed = true;
        ClosedAt = at;
        ClosedBy = by;
    }

    public void Reopen()
    {
        IsClosed = false;
        ClosedAt = null;
        ClosedBy = null;
    }
}

internal enum JournalEntryStatus
{
    Draft,
    Posted,
    Reversed,
}

internal sealed class JournalEntry : TenantEntity
{
    public const string ManualModule = "accounting";
    public const string ManualDocumentType = "manual";
    public const string ManualKind = "manual";

    private readonly List<JournalEntryLine> _lines = [];

    private JournalEntry()
    {
    }

    public JournalEntry(DateOnly date, string description, SourceRef? source, string kind)
    {
        Date = date;
        Description = description.Trim();
        Status = JournalEntryStatus.Draft;
        PostingKind = kind;
        if (source is null)
        {
            SourceModule = ManualModule;
            SourceDocumentType = ManualDocumentType;
            SourceDocumentId = Id;
        }
        else
        {
            SourceModule = source.Module;
            SourceDocumentType = source.DocumentType;
            SourceDocumentId = source.DocumentId;
            SourceDocumentNumber = source.DocumentNumber;
        }
    }

    public string? EntryNumber { get; private set; }

    public DateOnly Date { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public JournalEntryStatus Status { get; private set; }

    public string SourceModule { get; private set; } = string.Empty;

    public string SourceDocumentType { get; private set; } = string.Empty;

    public Guid SourceDocumentId { get; private set; }

    public string? SourceDocumentNumber { get; private set; }

    public string PostingKind { get; private set; } = string.Empty;

    public decimal TotalDebit { get; private set; }

    public decimal TotalCredit { get; private set; }

    public Guid? ReversalOfId { get; private set; }

    public Guid? ReversedById { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<JournalEntryLine> Lines => _lines;

    public bool IsManual => SourceModule == ManualModule && SourceDocumentType == ManualDocumentType;

    public void ReplaceDraft(DateOnly date, string description, string? referenceNumber, IEnumerable<JournalEntryLine> lines)
    {
        EnsureDraft();
        Date = date;
        Description = description.Trim();
        SourceDocumentNumber = referenceNumber;
        _lines.Clear();
        AddLines(lines);
    }

    public void AddLines(IEnumerable<JournalEntryLine> lines)
    {
        EnsureDraft();
        foreach (var line in lines)
        {
            line.AttachTo(this, _lines.Count + 1);
            _lines.Add(line);
        }

        TotalDebit = _lines.Sum(l => l.Debit);
        TotalCredit = _lines.Sum(l => l.Credit);
    }

    public void MarkPosted(string entryNumber, DateTimeOffset at, Guid? by)
    {
        EnsureDraft();
        EntryNumber = entryNumber;
        Status = JournalEntryStatus.Posted;
        PostedAt = at;
        PostedBy = by;
    }

    public void MarkAsReversalOf(JournalEntry original) => ReversalOfId = original.Id;

    public void MarkReversed(Guid reversalId)
    {
        if (Status != JournalEntryStatus.Posted)
        {
            throw ErpException.Conflict("entry_not_posted", "Only posted entries can be reversed.", "لا يمكن عكس إلا القيود المرحّلة.");
        }

        Status = JournalEntryStatus.Reversed;
        ReversedById = reversalId;
    }

    public void EnsureDraft()
    {
        if (Status != JournalEntryStatus.Draft)
        {
            throw ErpException.Conflict("entry_not_draft", "Posted entries cannot be changed; reverse them instead.", "لا يمكن تعديل قيد مرحّل، استخدم القيد العكسي.");
        }
    }
}

internal sealed class JournalEntryLine : TenantEntity
{
    private JournalEntryLine()
    {
    }

    public JournalEntryLine(Guid accountId, decimal debit, decimal credit, Guid? costCenterId, PartyType? partyType, Guid? partyId, string? notes)
    {
        AccountId = accountId;
        Debit = Math.Round(debit, 2, MidpointRounding.AwayFromZero);
        Credit = Math.Round(credit, 2, MidpointRounding.AwayFromZero);
        CostCenterId = costCenterId;
        PartyType = partyType;
        PartyId = partyId;
        Notes = notes;
    }

    public Guid JournalEntryId { get; private set; }

    public int LineNo { get; private set; }

    public Guid AccountId { get; private set; }

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }

    public Guid? CostCenterId { get; private set; }

    public PartyType? PartyType { get; private set; }

    public Guid? PartyId { get; private set; }

    public string? Notes { get; private set; }

    public void AttachTo(JournalEntry entry, int lineNo)
    {
        JournalEntryId = entry.Id;
        LineNo = lineNo;
    }

    public JournalEntryLine Mirror() => new(AccountId, Credit, Debit, CostCenterId, PartyType, PartyId, Notes);
}

/// <summary>Debit/credit totals per account and period, kept current by the posting engine (atomic increments).</summary>
internal sealed class AccountBalance : TenantEntity
{
    private AccountBalance()
    {
    }

    public Guid AccountId { get; private set; }

    public Guid FiscalPeriodId { get; private set; }

    public decimal Debit { get; private set; }

    public decimal Credit { get; private set; }
}

internal sealed class PostingAccountMapping : TenantEntity
{
    private PostingAccountMapping()
    {
    }

    public PostingAccountMapping(PostingPurpose purpose, Guid accountId)
    {
        Purpose = purpose;
        AccountId = accountId;
    }

    public PostingPurpose Purpose { get; private set; }

    public Guid AccountId { get; private set; }

    public void Remap(Guid accountId) => AccountId = accountId;
}
