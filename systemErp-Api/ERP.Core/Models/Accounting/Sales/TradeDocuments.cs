namespace ERP.Core.Models.Accounting;

// مستندات التجارة العامة: جدول رأس مستقل وجدول بنود مستقل لكل نوع، منفصلة عن فواتير المعارض (Invoice).
// الحقول والمنطق مشتركان عبر InvoiceBase/InvoiceItemBase؛ الجداول المرجعية (العملاء، الموردون، الأصناف، الحسابات) مشتركة.

/// <summary>فاتورة مبيعات (التجارة العامة ونقاط البيع).</summary>
public class SalesInvoice : InvoiceBase<SalesInvoiceItem>
{
    /// <summary>
    /// حالة التسليم «غير مسلّمة»: الترحيل يقيّد الإيراد والذمم والضريبة فقط، بلا حركة مخزون ولا تكلفة مبيعات. الأصناف المادية تخرج
    /// ببيانات التسليم من الفاتورة (من موقع صرف كل سطر) وتُرحَّل تكلفتها معها. false (الافتراضي، والفواتير القائمة) = «مسلّمة»: الصرف فوراً.
    /// </summary>
    public bool DeliveryDeferred { get; set; }
}

public class SalesInvoiceItem : InvoiceItemBase
{
    public virtual SalesInvoice Invoice { get; set; } = null!;
}

/// <summary>فاتورة مشتريات.</summary>
public class PurchaseInvoice : InvoiceBase<PurchaseInvoiceItem>
{
    /// <summary>
    /// حالة الاستلام «غير مستلمة»: الترحيل يقيّد المورد والضريبة، وقيمة الأصناف على «بضاعة مشتراة لم تُستلم» (1145) بلا حركة مخزون.
    /// بيان الاستلام من الفاتورة يُدخل الأصناف لموقع كل سطر بتكلفتها وينقل قيمتها من 1145 إلى مخزون الموقع. false (الافتراضي) = «مستلمة».
    /// </summary>
    public bool DeliveryDeferred { get; set; }
}

public class PurchaseInvoiceItem : InvoiceItemBase
{
    public virtual PurchaseInvoice Invoice { get; set; } = null!;
}

/// <summary>مرتجع مبيعات (إشعار دائن) على فاتورة مبيعات.</summary>
public class SalesReturn : InvoiceBase<SalesReturnItem> { }

public class SalesReturnItem : InvoiceItemBase
{
    public virtual SalesReturn Invoice { get; set; } = null!;
}

/// <summary>مرتجع مشتريات (إشعار مدين) على فاتورة مشتريات.</summary>
public class PurchaseReturn : InvoiceBase<PurchaseReturnItem> { }

public class PurchaseReturnItem : InvoiceItemBase
{
    public virtual PurchaseReturn Invoice { get; set; } = null!;
}
