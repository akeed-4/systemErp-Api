
namespace ERP.Core.Models.Accounting;

public class Invoice : BaseEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid Uuid { get; set; } = Guid.NewGuid();
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
    
    public string Status { get; set; } = "draft"; // draft | posted | cancelled
    public ZatcaSubmissionStatus ZatcaStatus { get; set; } = ZatcaSubmissionStatus.NotSubmitted;
    
    public string? ZatcaHash { get; set; }
    public string? ZatcaQrCode { get; set; }
    public string? ZatcaUblXml { get; set; }
    public string? ZatcaPih { get; set; }
    public string? ZatcaValidationMessages { get; set; }
    
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }

    /// <summary>وثيقة المصدر التي أُنشئت الفاتورة منها تلقائياً (أمر توريد، عقد بيع سيارة، ...).</summary>
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }

    public bool IsReturn { get; set; }
    public Guid? OriginalInvoiceId { get; set; }
    public string? OriginalInvoiceNumber { get; set; }
    public string? ReturnReason { get; set; }

    // حسابات بديلة للترحيل (فارغة = الحسابات الافتراضية): مثال فاتورة سيارات تستخدم إيراد/تكلفة/مخزون السيارات.
    public string? RevenueAccountCode { get; set; }
    public string? InventoryAccountCode { get; set; }
    public string? CogsAccountCode { get; set; }

    public virtual ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
    public virtual ICollection<InvoicePaymentSplit> PaymentSplits { get; set; } = new List<InvoicePaymentSplit>();
}
