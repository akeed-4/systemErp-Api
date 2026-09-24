namespace ERP.Core.DTOs.Accounting;

public partial class InvoiceDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid Uuid { get; set; }
    public DateTime IssueDate { get; set; }
    public string IssueTime { get; set; } = string.Empty;
    public InvoiceKind Kind { get; set; } = InvoiceKind.Sales;
    public InvoiceType InvoiceType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public Guid? PartyId { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }
    public string? PartyVatNumber { get; set; }
    public string? PartyCrNumber { get; set; }
    public string? PartyAddress { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public bool IsSplitPayment { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1.0m;
    public decimal Subtotal { get; set; }
    public decimal ItemsDiscountTotal { get; set; }
    public decimal InvoiceDiscount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalCost { get; set; }
    public decimal GrossProfit { get; set; }
    public string Status { get; set; } = "draft";
    public ZatcaSubmissionStatus ZatcaStatus { get; set; } = ZatcaSubmissionStatus.NotSubmitted;
    public string? ZatcaHash { get; set; }
    public string? ZatcaQrCode { get; set; }
    public string? ZatcaUblXml { get; set; }
    public string? ZatcaPih { get; set; }
    public string? ZatcaValidationMessages { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public bool IsReturn { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    public string? ReturnReason { get; set; }
    public string? RevenueAccountCode { get; set; }
    public string? InventoryAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }
    public List<InvoiceItemDto> Items { get; set; } = new();
    public List<InvoicePaymentSplitDto> PaymentSplits { get; set; } = new();
}
