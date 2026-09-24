using Erp.Modules.EInvoicing.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Modules.EInvoicing.Domain;

internal enum ZatcaEnvironment
{
    Sandbox,
    Simulation,
    Production,
}

internal enum ZatcaPhase
{
    Phase1,
    Phase2,
}

internal enum ComplianceStatus
{
    NotEnrolled,
    InProgress,
    Compliant,
}

internal enum ConnectionTestStatus
{
    Untested,
    Success,
    Failed,
}

/// <summary>
/// einvoicing.EInvoicingDevices: one ZATCA EGS unit (signing device) with its own invoice counter and hash chain.
/// The back-office device is the default; each POS terminal gets its own (Phase 10). Secrets are stored encrypted.
/// </summary>
internal sealed class EInvoicingDevice : TenantEntity
{
    public const string DefaultSerialNumber = "BACKOFFICE-01";

    /// <summary>ZATCA's PIH for the first invoice of a chain: base64(SHA-256("0")) in hex form.</summary>
    public const string InitialPreviousHash = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==";

    private EInvoicingDevice()
    {
    }

    public EInvoicingDevice(string serialNumber, string name, bool isDefault)
    {
        SerialNumber = serialNumber.Trim().ToUpperInvariant();
        Name = name;
        IsDefault = isDefault;
        IsEnabled = true;
        LastInvoiceHash = InitialPreviousHash;
    }

    public string SerialNumber { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public bool IsDefault { get; private set; }

    public bool IsEnabled { get; private set; }

    public ZatcaEnvironment Environment { get; private set; } = ZatcaEnvironment.Sandbox;

    public ZatcaPhase Phase { get; private set; } = ZatcaPhase.Phase1;

    public ComplianceStatus ComplianceStatus { get; private set; } = ComplianceStatus.NotEnrolled;

    public bool AutoSubmit { get; private set; }

    public string SolutionName { get; private set; } = "SystemErp";

    public string SolutionVersion { get; private set; } = "1.0";

    public string? TaxRegistrationNumber { get; private set; }

    public string? CsrCommonName { get; private set; }

    public string? OrganizationUnit { get; private set; }

    public string? OrganizationName { get; private set; }

    public string CountryCode { get; private set; } = "SA";

    public string? BusinessCategory { get; private set; }

    public string? CustomEndpointUrl { get; private set; }

    public string? CsidEncrypted { get; private set; }

    public string? SecretEncrypted { get; private set; }

    public string? CertificatePem { get; private set; }

    public string? PrivateKeyEncrypted { get; private set; }

    /// <summary>Invoice counter value (ICV) of the last registered document.</summary>
    public long LastIcv { get; private set; }

    public string LastInvoiceHash { get; private set; } = InitialPreviousHash;

    public DateTimeOffset? LastTestAt { get; private set; }

    public ConnectionTestStatus LastTestStatus { get; private set; }

    public int? LastTestLatencyMs { get; private set; }

    public string? LastTestMessage { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Configure(DeviceSettings settings)
    {
        Name = settings.Name ?? Name;
        IsEnabled = settings.IsEnabled;
        Environment = settings.Environment;
        Phase = settings.Phase;
        AutoSubmit = settings.AutoSubmit;
        SolutionName = settings.SolutionName ?? SolutionName;
        SolutionVersion = settings.SolutionVersion ?? SolutionVersion;
        TaxRegistrationNumber = settings.TaxRegistrationNumber;
        CsrCommonName = settings.CsrCommonName;
        OrganizationUnit = settings.OrganizationUnit;
        OrganizationName = settings.OrganizationName;
        CountryCode = settings.CountryCode ?? CountryCode;
        BusinessCategory = settings.BusinessCategory;
        CustomEndpointUrl = settings.CustomEndpointUrl;
    }

    public void SetSecrets(string? csidEncrypted, string? secretEncrypted, string? certificatePem, string? privateKeyEncrypted)
    {
        CsidEncrypted = csidEncrypted ?? CsidEncrypted;
        SecretEncrypted = secretEncrypted ?? SecretEncrypted;
        CertificatePem = certificatePem ?? CertificatePem;
        PrivateKeyEncrypted = privateKeyEncrypted ?? PrivateKeyEncrypted;
        ComplianceStatus = CsidEncrypted is not null && PrivateKeyEncrypted is not null ? ComplianceStatus.InProgress : ComplianceStatus;
    }

    public (long Icv, string PreviousHash) NextInChain() => (LastIcv + 1, LastInvoiceHash);

    public void Advance(long icv, string invoiceHash)
    {
        LastIcv = icv;
        LastInvoiceHash = invoiceHash;
    }

    public void RecordTest(DateTimeOffset at, bool success, int latencyMs, string message)
    {
        LastTestAt = at;
        LastTestStatus = success ? ConnectionTestStatus.Success : ConnectionTestStatus.Failed;
        LastTestLatencyMs = latencyMs;
        LastTestMessage = message;
    }
}

internal sealed record DeviceSettings(
    string? Name,
    bool IsEnabled,
    ZatcaEnvironment Environment,
    ZatcaPhase Phase,
    bool AutoSubmit,
    string? SolutionName,
    string? SolutionVersion,
    string? TaxRegistrationNumber,
    string? CsrCommonName,
    string? OrganizationUnit,
    string? OrganizationName,
    string? CountryCode,
    string? BusinessCategory,
    string? CustomEndpointUrl);

/// <summary>einvoicing.EInvoiceDocuments: one registered tax invoice / note in a device chain.</summary>
internal sealed class EInvoiceDocument : TenantEntity
{
    private EInvoiceDocument()
    {
    }

    public EInvoiceDocument(Guid deviceId, EInvoiceRequest request, long icv, string previousHash, string invoiceHash, string qrCode)
    {
        DeviceId = deviceId;
        SourceModule = request.SourceModule;
        SourceDocumentType = request.SourceDocumentType;
        SourceDocumentId = request.SourceDocumentId;
        DocumentNumber = request.DocumentNumber;
        Kind = request.Kind;
        IssuedAt = request.IssuedAt;
        TotalWithVat = request.TotalWithVat;
        VatTotal = request.VatTotal;
        BuyerName = request.BuyerName;
        BuyerVatNumber = request.BuyerVatNumber;
        OriginalDocumentNumber = request.OriginalDocumentNumber;
        Uuid = Guid.NewGuid();
        Icv = icv;
        PreviousInvoiceHash = previousHash;
        InvoiceHash = invoiceHash;
        QrCode = qrCode;
        Status = EInvoiceStatus.NotSubmitted;
    }

    public Guid DeviceId { get; private set; }

    public string SourceModule { get; private set; } = string.Empty;

    public string SourceDocumentType { get; private set; } = string.Empty;

    public Guid SourceDocumentId { get; private set; }

    public string DocumentNumber { get; private set; } = string.Empty;

    public EInvoiceKind Kind { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public decimal TotalWithVat { get; private set; }

    public decimal VatTotal { get; private set; }

    public string? BuyerName { get; private set; }

    public string? BuyerVatNumber { get; private set; }

    public string? OriginalDocumentNumber { get; private set; }

    public Guid Uuid { get; private set; }

    public long Icv { get; private set; }

    public string PreviousInvoiceHash { get; private set; } = string.Empty;

    public string InvoiceHash { get; private set; } = string.Empty;

    public string QrCode { get; private set; } = string.Empty;

    public EInvoiceStatus Status { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public string? ResponseMessage { get; private set; }

    public bool IsStandard => Kind is EInvoiceKind.StandardTaxInvoice or EInvoiceKind.StandardCreditNote or EInvoiceKind.StandardDebitNote;

    public void RecordSubmission(EInvoiceStatus status, DateTimeOffset at, string message)
    {
        Status = status;
        SubmittedAt = at;
        ResponseMessage = message;
    }
}
