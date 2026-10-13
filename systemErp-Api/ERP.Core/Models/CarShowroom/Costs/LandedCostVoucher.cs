namespace ERP.Core.Models.CarShowroom;

public enum LandedCostVoucherStatus
{
    Draft = 1,
    Approved = 2,
    Cancelled = 3
}

/// <summary>
/// سند التكاليف الإضافية: فاتورة مورد خدمات (شاحن، مخلّص، جمارك...) تُوزَّع على سيارة أو أكثر.
/// عند الاعتماد يتحول كل سطر إلى سطر مصروف فعلي يحل محل التقديري المقابل، ويُحفظ الفرق بين القيمتين.
/// </summary>
public class LandedCostVoucher : BaseEntity
{
    public string VoucherNumber { get; set; } = string.Empty;
    /// <summary>تاريخ فاتورة المورد؛ سعر الصرف سعر هذا التاريخ.</summary>
    public DateTime Date { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;
    /// <summary>أمر الشراء الذي تخصه الفاتورة (اختياري): تُطابَق أسطره التقديرية أولاً.</summary>
    public Guid? ProcurementOrderId { get; set; }
    public string? ProcurementOrderNumber { get; set; }
    public LandedCostVoucherStatus Status { get; set; } = LandedCostVoucherStatus.Draft;
    public string? Notes { get; set; }

    // بعملة السند
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    /// <summary>إجمالي ما يُحمَّل كتكلفة/مصروف بالعملة الأساسية.</summary>
    public decimal BaseCostTotal { get; set; }

    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? JournalEntryId { get; set; }

    public virtual ICollection<LandedCostVoucherLine> Lines { get; set; } = new List<LandedCostVoucherLine>();
}
