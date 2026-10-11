namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// أنماط احتساب ضريبة القيمة المضافة على مبيعات السيارات. أسماء الأعضاء بشرطة سفلية لتُسلسَل تماماً كما
/// تتوقعها الواجهة (standard_15 / profit_margin_15 / margin_scheme / exempt).
/// </summary>
public enum VatMode
{
    Standard_15 = 1,      // ضريبة قياسية 15% على كامل القيمة
    ProfitMargin_15 = 2,  // ضريبة هامش الربح (سيارات مستعملة): الهامش × 15/115 مضمَّنة في سعر البيع
    MarginScheme = 3,     // مرادف لـ ProfitMargin_15 (قيمة قديمة في بعض الشاشات)
    Exempt = 4            // معفى من الضريبة
}
