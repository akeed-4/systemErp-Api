
namespace ERP.Core.Models.Accounting;

public class CommercialOrder : BaseEntity, IScopedRecord, IPartyDocument
{
    /// <summary>منشئ المستند (يُختم آلياً) لنطاق «السجلات التي أنشأها».</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>المستخدم المسند إليه المستند لنطاق «السجلات المسندة إليه».</summary>
    public Guid? AssignedToUserId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_order"; // sales_order, purchase_order
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
    public string? PartyVatNumber { get; set; }
    /// <summary>المستودع الذي ستُصرف منه/تُستلم فيه الأصناف؛ ينتقل إلى الفاتورة (فارغ = الافتراضي).</summary>
    public Guid? WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime ExpectedDeliveryDate { get; set; }
    public string? PaymentTerms { get; set; }
    
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    
    public string Status { get; set; } = "draft";
    public Guid? ConvertedInvoiceId { get; set; }
    public string? Notes { get; set; }
    public Guid? AgreementId { get; set; }
    public string? AgreementNumber { get; set; }

    // ---------- أمر شراء من ترسية طلب عرض أسعار ----------
    public Guid? RfqId { get; set; }
    /// <summary>عرض المورد المرسّى.</summary>
    public Guid? QuotationId { get; set; }
    /// <summary>طلب الشراء الأصلي.</summary>
    public Guid? RequisitionId { get; set; }
    /// <summary>التكاليف الإضافية المرسّاة من العرض (قبل الضريبة)؛ موزعة في سعر الاستلام لكل سطر.</summary>
    public decimal ExtraCosts { get; set; }

    public virtual ICollection<CommercialOrderItem> Items { get; set; } = new List<CommercialOrderItem>();
}
