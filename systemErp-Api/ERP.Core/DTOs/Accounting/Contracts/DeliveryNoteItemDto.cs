namespace ERP.Core.DTOs.Accounting;

public partial class DeliveryNoteItemDto
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal ContractQty { get; set; }
    public decimal DeliveredQty { get; set; }
    public decimal ReturnedQty { get; set; }
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>خصم السطر مبلغاً قبل الضريبة.</summary>
    public decimal Discount { get; set; }
    /// <summary>موقع السطر (فارغ = موقع البيان).</summary>
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>للقراءة: الاسم الإنجليزي من بطاقة الصنف.</summary>
    public string? ItemNameEn { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    /// <summary>بند جدول كميات المشروع لسند استلام مشروع؛ فارغ = من سطر الطلب.</summary>
    public Guid? BoqItemId { get; set; }
    /// <summary>سطر المصدر: بند الاتفاقية أو سطر فاتورة البيع (إلزامي لبيان من فاتورة).</summary>
    public Guid? SourceLineId { get; set; }
    /// <summary>للقراءة: الكمية المؤكد تسليمها فعلياً (فارغ = لم تُؤكد).</summary>
    public decimal? ConfirmedQty { get; set; }
    /// <summary>للقراءة: ما فُوتر منها في فواتير مرحلية.</summary>
    public decimal InvoicedQty { get; set; }
}
