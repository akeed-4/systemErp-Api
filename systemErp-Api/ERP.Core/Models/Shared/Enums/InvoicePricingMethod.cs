namespace ERP.Core.Models.Shared;

/// <summary>
/// طريقة تسعير الفاتورة: كيف يُقرأ سعر الوحدة والخصم المُدخلان. تُحفظ على الفاتورة فتبقى حساباتها التاريخية كما صدرت،
/// والمرتجع يرث طريقة أصله. الأرقام المحفوظة على السطر (TotalBeforeVat/VatAmount/TotalAfterVat) صافية دائماً في الطريقتين.
/// </summary>
public enum InvoicePricingMethod
{
    /// <summary>السعر والخصم قبل الضريبة (سلوك كل الفواتير القائمة).</summary>
    Exclusive = 1,
    /// <summary>السعر والخصم شاملان ضريبة القيمة المضافة؛ يُستخرج الصافي ثم تُحسب الضريبة عليه.</summary>
    Inclusive = 2,
}
