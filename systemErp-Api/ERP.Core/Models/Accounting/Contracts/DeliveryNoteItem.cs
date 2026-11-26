
namespace ERP.Core.Models.Accounting;

public class DeliveryNoteItem : BaseEntity
{
    public Guid DeliveryNoteId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal ContractQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal ReturnedQty { get; set; }
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>خصم السطر مبلغاً قبل الضريبة (من الكمية المسلّمة × السعر)، وينتقل إلى سطر الفاتورة.</summary>
    public decimal Discount { get; set; }
    /// <summary>موقع السطر (فارغ = موقع البيان)؛ ينتقل إلى سطر الفاتورة.</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary>مركز تكلفة السطر؛ ينتقل إلى سطر الفاتورة.</summary>
    public Guid? CostCenterId { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    /// <summary>بند القياس في جدول كميات المشروع لسند استلام مشروع (من سطر الطلب).</summary>
    public Guid? BoqItemId { get; set; }

    /// <summary>سطر المصدر: بند الاتفاقية أو سطر فاتورة البيع.</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>
    /// الكمية المسلّمة فعلياً والمؤكدة بإثبات الاستلام (فارغ = لم تُؤكد). <see cref="DeliveredQty"/> يبقى كمية البيان كما هو.
    /// </summary>
    public decimal? ConfirmedQty { get; set; }
    /// <summary>ما فُوتر من الكمية المؤكدة في فواتير مرحلية (بيان اتفاقية متتبَّع).</summary>
    public decimal InvoicedQty { get; set; }

    // ---------- استلام المشتريات: المستلم والمرفوض (الهجرة PurchaseReceiptTracking) ----------
    /// <summary>
    /// المستلم فعلياً في سند الاستلام = المقبول (<see cref="DeliveredQty"/>) + المرفوض. فارغ (السجلات القديمة وبيانات المبيعات) = يساوي <see cref="DeliveredQty"/>.
    /// </summary>
    public decimal? ReceivedQty { get; set; }
    /// <summary>المرفوض عند الاستلام: لا يدخل كمية السند ولا فوترته ولا المخزون؛ في سند فاتورة المشتريات ينتظر مرتجع مشتريات مالياً.</summary>
    public decimal RejectedQty { get; set; }
    public string? RejectionReason { get; set; }

    public virtual DeliveryNote DeliveryNote { get; set; } = null!;
}
