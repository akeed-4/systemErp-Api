namespace Erp.Modules.Sales.Contracts;

/// <summary>ZATCA type: standard tax invoice (B2B, buyer VAT required) or simplified (B2C).</summary>
public enum SalesInvoiceType
{
    TaxInvoice,
    Simplified,
}

/// <summary>Cash = paid now (payment rows required); credit = on the customer's account.</summary>
public enum SettlementType
{
    Cash,
    Credit,
}

public sealed record SalesLineInput(
    Guid? ProductId,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount = 0,
    decimal? VatRate = null,
    string? Unit = null,
    Guid? CostCenterId = null);

public sealed record SalesPaymentInput(Guid PaymentMethodId, decimal Amount, Guid? BankAccountId = null, string? Reference = null);

/// <summary>The document that asked for the invoice (contract milestone, delivery note …), kept for traceability.</summary>
public sealed record SalesSourceRef(string Module, string DocumentType, Guid DocumentId, string DocumentNumber);

/// <param name="RevenueAccountId">Overrides the SalesRevenue account (e.g. a contract's revenue account).</param>
public sealed record CreateSalesInvoiceCommand(
    Guid? CustomerId,
    SettlementType Settlement,
    IReadOnlyList<SalesLineInput> Lines,
    IReadOnlyList<SalesPaymentInput>? Payments = null,
    DateOnly? Date = null,
    SalesInvoiceType? InvoiceType = null,
    decimal InvoiceDiscount = 0,
    Guid? WarehouseId = null,
    Guid? CostCenterId = null,
    string? BuyerName = null,
    string? Notes = null,
    SalesSourceRef? Source = null,
    Guid? QuotationId = null,
    Guid? SalesOrderId = null,
    Guid? RevenueAccountId = null,
    bool Post = true);

public sealed record SalesInvoiceResult(Guid InvoiceId, string? InvoiceNumber, decimal NetTotal, decimal VatTotal, decimal GrandTotal, Guid? JournalEntryId, string? QrCode);

/// <summary>General sales invoices. Contracts/delivery notes create their invoices through it (§11.2: Contracts → Sales).</summary>
public interface ISalesInvoiceService
{
    Task<SalesInvoiceResult> CreateAsync(CreateSalesInvoiceCommand command, CancellationToken cancellationToken);
}
