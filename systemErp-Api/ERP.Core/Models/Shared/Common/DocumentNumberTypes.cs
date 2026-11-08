namespace ERP.Core.Models.Shared;

/// <summary>نوع مستند قابل لإعداد ترقيمه: مفتاح عدّاده، اسمه، وحدته، بادئته الافتراضية، وهل يدعم الإدخال اليدوي.</summary>
public sealed record DocumentNumberType(string Key, string NameAr, string NameEn, string Module, string DefaultPrefix, bool ManualAllowed);

/// <summary>
/// أنواع المستندات المعروضة في إعداد الترقيم. مفاتيحها هي مفاتيح العدّادات نفسها التي تستخدمها الخدمات.
/// نوع غير مدرج يبقى على سلوكه القائم (بادئة صفه ونطاق المنشأة العام). القيود المولدة آلياً تبقى تلقائية دائماً:
/// اليدوي في journal_entry للقيود اليدوية فقط.
/// </summary>
public static class DocumentNumberTypes
{
    public static readonly IReadOnlyList<DocumentNumberType> All = new DocumentNumberType[]
    {
        new("sales_invoice", "فواتير المبيعات", "Sales invoices", "sales", "SINV-", true),
        new("sales_return", "مرتجعات المبيعات", "Sales returns", "sales", "SRET-", true),
        new("sales_quotation", "عروض أسعار المبيعات", "Sales quotations", "sales", "QT-", true),
        new("purchase_invoice", "فواتير المشتريات", "Purchase invoices", "purchases", "PINV-", true),
        new("purchase_return", "مرتجعات المشتريات", "Purchase returns", "purchases", "PRET-", true),
        new("purchase_quotation", "عروض أسعار المشتريات", "Purchase quotations", "purchases", "PQT-", true),
        new("purchase_order", "أوامر الشراء", "Purchase orders", "purchases", "PO-", true),
        new("receipt_voucher", "سندات القبض", "Receipt vouchers", "accounting", "RV-", true),
        new("payment_voucher", "سندات الصرف", "Payment vouchers", "accounting", "PV-", true),
        new("journal_entry", "القيود اليومية", "Journal entries", "accounting", "JE-", true),
        new("material_requisition", "طلبات الشراء", "Purchase requisitions", "purchases", "REQ-", true),
        new("commercial_contract", "العقود", "Contracts", "contracts", "CON-", true),
        new("agreement", "الاتفاقيات", "Agreements", "contracts", "AGR-", true),
        new("delivery_note", "بيانات التسليم", "Delivery notes", "contracts", "DN-", true),
        new("goods_receipt", "سندات الاستلام", "Goods receipt notes", "contracts", "GRN-", true),
        new("delivery_return", "مرتجعات بيانات التسليم", "Delivery returns", "contracts", "DR-", true),
        new("contract_certificate", "مستخلصات الأعمال", "Progress certificates", "contracts", "CRT-", false),
        new("contract_variation", "أوامر التغيير", "Variation orders", "contracts", "VO-", false),
        new("inventory_count", "الجرد", "Inventory counts", "inventory", "IC-", true),
        new("stock_transfer", "التحويلات المخزنية", "Stock transfers", "inventory", "TRF-", true),
        new("production_order", "مستندات التصنيع", "Production orders", "inventory", "MFG-", true),
        new("project_material_issue", "صرف مواد للمشاريع", "Project material issues", "contracts", "PMI-", true),
        new("car_procurement_order", "أوامر شراء السيارات", "Car purchase orders", "showroom", "CPO-", true),
        new("car_sales_contract", "عقود بيع السيارات", "Car sales contracts", "showroom", "CSC-", true),
    };

    public static DocumentNumberType? Find(string key) => All.FirstOrDefault(t => t.Key == key);
}
