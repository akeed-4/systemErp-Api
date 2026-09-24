using Erp.Modules.Payments.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Errors;

namespace Erp.Modules.Payments.Domain;

/// <summary>payments.PaymentMethods: the one definition of each payment method, used by every module (§14 D10).</summary>
internal sealed class PaymentMethod : TenantEntity
{
    private PaymentMethod()
    {
    }

    public PaymentMethod(string code) => Code = code.Trim().ToUpperInvariant();

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public PaymentMethodType Type { get; private set; }

    public PaymentChannel Channel { get; private set; }

    /// <summary>GL account the money lands in; null for credit (on account) or until configured.</summary>
    public Guid? AccountId { get; private set; }

    /// <summary>Default company bank account for bank-type methods.</summary>
    public Guid? BankAccountId { get; private set; }

    public string? Icon { get; private set; }

    public decimal CommissionPercent { get; private set; }

    public bool RequiresReference { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string nameAr, string nameEn, PaymentMethodType type, PaymentChannel channel, Guid? accountId, Guid? bankAccountId, string? icon, decimal commission, bool requiresReference, bool isActive)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        Type = type;
        Channel = channel;
        AccountId = accountId;
        BankAccountId = bankAccountId;
        Icon = icon;
        CommissionPercent = commission;
        RequiresReference = requiresReference;
        IsActive = isActive;
    }
}

internal enum VoucherStatus
{
    Draft,
    Posted,
    Cancelled,
}

/// <summary>payments.Vouchers: receipt (سند قبض) or payment (سند صرف), possibly split across several payment methods.</summary>
internal sealed class Voucher : TenantEntity
{
    private readonly List<VoucherPayment> _payments = [];
    private readonly List<VoucherAllocation> _allocations = [];

    private Voucher()
    {
    }

    public Voucher(VoucherType type, DateOnly date)
    {
        Type = type;
        Date = date;
        Status = VoucherStatus.Draft;
    }

    public string? VoucherNumber { get; private set; }

    public VoucherType Type { get; private set; }

    public DateOnly Date { get; private set; }

    public decimal Amount { get; private set; }

    public string AmountInWordsAr { get; private set; } = string.Empty;

    public VoucherPartyType PartyType { get; private set; }

    public Guid? PartyId { get; private set; }

    public string PartyName { get; private set; } = string.Empty;

    public Guid PartyAccountId { get; private set; }

    public string? ReferenceNumber { get; private set; }

    public string? Notes { get; private set; }

    public string? ReceivedOrPaidBy { get; private set; }

    public Guid? CostCenterId { get; private set; }

    public VoucherStatus Status { get; private set; }

    public Guid? JournalEntryId { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<VoucherPayment> Payments => _payments;

    public IReadOnlyList<VoucherAllocation> Allocations => _allocations;

    public void SetParty(VoucherPartyType type, Guid? partyId, string partyName, Guid partyAccountId)
    {
        PartyType = type;
        PartyId = partyId;
        PartyName = partyName.Trim();
        PartyAccountId = partyAccountId;
    }

    public void SetDetails(string? referenceNumber, string? notes, string? receivedOrPaidBy, Guid? costCenterId)
    {
        ReferenceNumber = referenceNumber;
        Notes = notes;
        ReceivedOrPaidBy = receivedOrPaidBy;
        CostCenterId = costCenterId;
    }

    public void AddPayment(Guid paymentMethodId, Guid? bankAccountId, Guid treasuryAccountId, decimal amount, string? reference)
    {
        if (amount <= 0)
        {
            throw ErpException.Validation("Every payment amount must be positive.", "يجب أن يكون مبلغ كل دفعة أكبر من صفر.");
        }

        _payments.Add(new VoucherPayment(Id, _payments.Count + 1, paymentMethodId, bankAccountId, treasuryAccountId, Math.Round(amount, 2, MidpointRounding.AwayFromZero), reference));
        Amount = _payments.Sum(p => p.Amount);
    }

    public void Allocate(VoucherAllocationInput allocation) =>
        _allocations.Add(new VoucherAllocation(Id, allocation.Module, allocation.DocumentType, allocation.DocumentId, allocation.DocumentNumber, allocation.Amount));

    public void SetWords(string words) => AmountInWordsAr = words;

    public void MarkPosted(string number, Guid journalEntryId)
    {
        VoucherNumber = number;
        JournalEntryId = journalEntryId;
        Status = VoucherStatus.Posted;
    }

    public void AssignNumber(string number) => VoucherNumber = number;

    public void Cancel(DateTimeOffset at, string reason)
    {
        if (Status == VoucherStatus.Cancelled)
        {
            throw ErpException.Conflict("voucher_cancelled", "The voucher is already cancelled.", "السند ملغى مسبقاً.");
        }

        Status = VoucherStatus.Cancelled;
        CancelledAt = at;
        CancellationReason = reason;
    }
}

internal sealed class VoucherPayment : TenantEntity
{
    private VoucherPayment()
    {
    }

    public VoucherPayment(Guid voucherId, int lineNo, Guid paymentMethodId, Guid? bankAccountId, Guid treasuryAccountId, decimal amount, string? reference)
    {
        VoucherId = voucherId;
        LineNo = lineNo;
        PaymentMethodId = paymentMethodId;
        BankAccountId = bankAccountId;
        TreasuryAccountId = treasuryAccountId;
        Amount = amount;
        Reference = reference;
    }

    public Guid VoucherId { get; private set; }

    public int LineNo { get; private set; }

    public Guid PaymentMethodId { get; private set; }

    public Guid? BankAccountId { get; private set; }

    /// <summary>The GL account the money came into / went out of (resolved at creation).</summary>
    public Guid TreasuryAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }
}

internal sealed class VoucherAllocation : TenantEntity
{
    private VoucherAllocation()
    {
    }

    public VoucherAllocation(Guid voucherId, string module, string documentType, Guid documentId, string documentNumber, decimal amount)
    {
        VoucherId = voucherId;
        TargetModule = module;
        TargetDocumentType = documentType;
        TargetDocumentId = documentId;
        TargetDocumentNumber = documentNumber;
        Amount = amount;
    }

    public Guid VoucherId { get; private set; }

    public string TargetModule { get; private set; } = string.Empty;

    public string TargetDocumentType { get; private set; } = string.Empty;

    public Guid TargetDocumentId { get; private set; }

    public string TargetDocumentNumber { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
}
