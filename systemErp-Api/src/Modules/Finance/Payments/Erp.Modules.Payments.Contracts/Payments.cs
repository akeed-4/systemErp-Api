namespace Erp.Modules.Payments.Contracts;

public enum PaymentMethodType
{
    Cash,
    Card,
    Bank,
    Cheque,
    Credit,
}

/// <summary>How the money moves; POS and receipts use it to label totals (cash, mada, card, Apple Pay …).</summary>
public enum PaymentChannel
{
    Cash,
    Mada,
    VisaMaster,
    ApplePay,
    Transfer,
    Cheque,
    Financing,
    Credit,
}

/// <param name="AccountId">GL account the money lands in (cash vault, card clearing, bank …); null for credit (on account).</param>
public sealed record PaymentMethodSummary(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    PaymentMethodType Type,
    PaymentChannel Channel,
    Guid? AccountId,
    Guid? BankAccountId,
    decimal CommissionPercent,
    bool RequiresReference,
    bool IsActive);

public interface IPaymentMethodDirectory
{
    Task<PaymentMethodSummary?> FindAsync(Guid paymentMethodId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, PaymentMethodSummary>> FindManyAsync(IReadOnlyCollection<Guid> paymentMethodIds, CancellationToken cancellationToken);

    /// <summary>Resolves the GL account one payment lands in: an explicit bank account wins, else the method's account.</summary>
    Task<Guid> ResolveTreasuryAccountAsync(Guid paymentMethodId, Guid? bankAccountId, CancellationToken cancellationToken);
}

public enum VoucherType
{
    Receipt,
    Payment,
}

public enum VoucherPartyType
{
    Customer,
    Supplier,
    Other,
}

public sealed record VoucherPaymentInput(Guid PaymentMethodId, decimal Amount, Guid? BankAccountId = null, string? Reference = null);

/// <summary>Links a voucher to the document it settles (an invoice, an installment …).</summary>
public sealed record VoucherAllocationInput(string Module, string DocumentType, Guid DocumentId, string DocumentNumber, decimal Amount);

/// <param name="PartyAccountId">Required for <see cref="VoucherPartyType.Other"/>; customers/suppliers use their own GL account.</param>
public sealed record CreateVoucherCommand(
    VoucherType Type,
    DateOnly Date,
    VoucherPartyType PartyType,
    Guid? PartyId,
    string? PartyName,
    Guid? PartyAccountId,
    IReadOnlyList<VoucherPaymentInput> Payments,
    string? ReferenceNumber = null,
    string? Notes = null,
    string? ReceivedOrPaidBy = null,
    Guid? CostCenterId = null,
    IReadOnlyList<VoucherAllocationInput>? Allocations = null,
    bool Post = true);

public sealed record VoucherResult(Guid VoucherId, string VoucherNumber, decimal Amount, Guid? JournalEntryId);

/// <summary>Receipt and payment vouchers. Other modules (car installments, supplier installments, invoices) create vouchers through it.</summary>
public interface IVoucherService
{
    Task<VoucherResult> CreateAsync(CreateVoucherCommand command, CancellationToken cancellationToken);

    /// <summary>Total of posted, non-cancelled vouchers allocated to a document.</summary>
    Task<decimal> GetAllocatedAmountAsync(string module, string documentType, Guid documentId, CancellationToken cancellationToken);
}
