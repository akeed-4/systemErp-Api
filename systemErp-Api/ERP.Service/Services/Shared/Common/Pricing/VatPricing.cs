namespace ERP.Service.Services.Shared;

/// <summary>
/// المصدر الموحّد لتحويل مبالغ الفاتورة المحفوظة إلى قيمها الصافية حسب طريقة تسعيرها: يستخدمه التسعير (<see cref="DocumentPricing"/>)
/// والمخزون (سعر بيع حركة الصنف) وهيئة الزكاة (سعر الوحدة الصافي والخصم في XML). القيد يُبنى من الأرقام الصافية المحفوظة على السطر.
/// الفاتورة غير الشاملة تعود بقيمها كما هي، فلا يتغير أي مستند قائم.
/// </summary>
public static class VatPricing
{
    /// <summary>صافي مبلغ شامل الضريبة بخانتين: Round(المبلغ × 100 ÷ (100 + النسبة)). النسبة صفر = المبلغ نفسه.</summary>
    public static decimal NetOf(decimal inclusiveAmount, decimal vatRatePercent)
        => vatRatePercent <= 0 ? DocumentPricing.Round(inclusiveAmount) : DocumentPricing.Round(inclusiveAmount * 100m / (100m + vatRatePercent));

    /// <summary>سعر الوحدة الصافي لسطر محفوظ (لحركة المخزون، بدقة عمود السعر 4 خانات). غير الشامل: السعر المحفوظ نفسه.</summary>
    public static decimal NetUnitPrice(InvoicePricingMethod method, decimal unitPrice, decimal vatRatePercent)
        => method != InvoicePricingMethod.Inclusive || vatRatePercent <= 0
            ? unitPrice
            : Math.Round(unitPrice * 100m / (100m + vatRatePercent), 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// سعر الوحدة الصافي والخصم (Allowance) لسطر الفاتورة في XML الهيئة، بحيث يتحقق دائماً:
    /// Round(الكمية × السعر) − الخصم = صافي السطر المحفوظ (<paramref name="totalBeforeVat"/>)، ولا يُرسل سعر شامل على أنه صافٍ.
    /// غير الشامل: السعر المحفوظ والخصم = Round(الكمية × السعر) − الصافي (كما كان). الشامل: صافي إجمالي السطر قبل الخصومات
    /// (<see cref="NetOf"/>) مقسوماً على الكمية بست خانات (دقة PriceAmount)، والخصم هو الفرق حتى الصافي المحفوظ.
    /// </summary>
    public static (decimal UnitPrice, decimal Allowance) ZatcaLinePrice(
        InvoicePricingMethod method, decimal quantity, decimal unitPrice, decimal vatRatePercent, decimal totalBeforeVat)
    {
        var netUnitPrice = method != InvoicePricingMethod.Inclusive || vatRatePercent <= 0 || quantity == 0
            ? unitPrice
            : Math.Round(NetOf(DocumentPricing.Round(quantity * unitPrice), vatRatePercent) / quantity, 6, MidpointRounding.AwayFromZero);
        return (netUnitPrice, Math.Max(0, DocumentPricing.Round(quantity * netUnitPrice) - totalBeforeVat));
    }
}
