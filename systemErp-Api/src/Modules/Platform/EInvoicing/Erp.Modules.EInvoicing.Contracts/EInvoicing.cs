namespace Erp.Modules.EInvoicing.Contracts;

/// <summary>ZATCA invoice types: standard (B2B, cleared) and simplified (B2C, reported), plus credit/debit notes of each.</summary>
public enum EInvoiceKind
{
    StandardTaxInvoice,
    SimplifiedTaxInvoice,
    StandardCreditNote,
    SimplifiedCreditNote,
    StandardDebitNote,
    SimplifiedDebitNote,
}

public enum EInvoiceStatus
{
    NotSubmitted,
    Reported,
    Cleared,
    Rejected,
    Warning,
}

/// <param name="DeviceId">The signing device (EGS unit); null = the tenant's default back-office device. POS terminals pass their own.</param>
public sealed record EInvoiceRequest(
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    string DocumentNumber,
    EInvoiceKind Kind,
    DateTimeOffset IssuedAt,
    decimal TotalWithVat,
    decimal VatTotal,
    string? BuyerName = null,
    string? BuyerVatNumber = null,
    string? OriginalDocumentNumber = null,
    Guid? DeviceId = null);

public sealed record EInvoiceResult(
    Guid DocumentId,
    Guid Uuid,
    long Icv,
    string InvoiceHash,
    string PreviousInvoiceHash,
    string QrCode,
    EInvoiceStatus Status);

/// <summary>
/// Registers every tax invoice / note in its device's ZATCA chain: UUID, invoice counter (ICV), previous invoice hash (PIH),
/// invoice hash and the TLV QR code. Idempotent per source document. Runs in the caller's unit of work.
/// Submission to ZATCA happens after commit (Phase 15: requires onboarding credentials).
/// </summary>
public interface IEInvoicingService
{
    Task<EInvoiceResult> RegisterAsync(EInvoiceRequest request, CancellationToken cancellationToken);

    Task<EInvoiceResult?> FindBySourceAsync(string sourceModule, string sourceDocumentType, Guid sourceDocumentId, CancellationToken cancellationToken);
}
