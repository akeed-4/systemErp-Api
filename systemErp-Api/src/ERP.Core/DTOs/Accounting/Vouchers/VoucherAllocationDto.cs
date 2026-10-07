namespace ERP.Core.DTOs.Accounting;

/// <summary>توزيع مبلغ من السند على فاتورة آجلة للطرف نفسه.</summary>
public partial class VoucherAllocationDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    /// <summary>يملؤه الخادم.</summary>
    public string? InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>فاتورة آجلة عليها متبقٍّ (لاختيارها عند السداد).</summary>
public class OpenInvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public decimal GrandTotal { get; set; }
    /// <summary>الجزء الآجل من الفاتورة عند ترحيلها.</summary>
    public decimal CreditAmount { get; set; }
    public decimal AmountDue { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
}
