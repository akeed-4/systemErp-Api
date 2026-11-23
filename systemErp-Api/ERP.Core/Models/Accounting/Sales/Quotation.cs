
namespace ERP.Core.Models.Accounting;

public class Quotation : BaseEntity, IScopedRecord, IPartyDocument
{
    /// <summary>منشئ المستند (يُختم آلياً) لنطاق «السجلات التي أنشأها».</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>المستخدم المسند إليه المستند لنطاق «السجلات المسندة إليه».</summary>
    public Guid? AssignedToUserId { get; set; }
    public string QuotationNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_quotation"; // sales_quotation, purchase_quotation
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }
    /// <summary>نسخة PartyId المقيّدة بمفتاح أجنبي إلى Customer (تُعبّأ تلقائياً عند الحفظ).</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>نسخة PartyId المقيّدة بمفتاح أجنبي إلى Supplier (تُعبّأ تلقائياً عند الحفظ).</summary>
    public Guid? SupplierId { get; set; }
    public bool IsSupplierParty => PartySide.IsSupplier(PartyType, Type.StartsWith("purchase"));
    public string PartyName { get; set; } = string.Empty;
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }
    public string? PartyVatNumber { get; set; }
    /// <summary>الموقع الذي ستُصرف منه/تُستلم فيه الأصناف؛ ينتقل إلى الفاتورة (فارغ = الافتراضي).</summary>
    public Guid? WarehouseId { get; set; }
    public DateTime Date { get; set; }
    public DateTime ValidUntil { get; set; }
    public string? PaymentTerms { get; set; }
    
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    
    public string Status { get; set; } = "draft";
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public string? TermsAndConditions { get; set; }
    /// <summary>طلب عرض الأسعار (RFQ) الذي قُدّم عليه هذا العرض من المورد؛ فارغ = عرض عادي.</summary>
    public Guid? RfqId { get; set; }
    /// <summary>تكاليف إضافية على العرض قبل الضريبة (شحن، تركيب…)؛ توزَّع على أسطره بنسبة صافي قيمتها في المقارنة والترسية.</summary>
    public decimal ExtraCosts { get; set; }
    public int? DeliveryDays { get; set; }

    public virtual ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}
