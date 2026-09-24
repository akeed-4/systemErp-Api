namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// أنماط احتساب ضريبة القيمة المضافة على مبيعات السيارات. أسماء الأعضاء بشرطة سفلية لتُسلسَل تماماً كما
/// تتوقعها الواجهة (standard_15 / profit_margin_15 / margin_scheme / exempt).
/// </summary>
public enum VatMode
{
    Standard_15 = 1,      // ضريبة قياسية 15% على كامل القيمة
    ProfitMargin_15 = 2,  // ضريبة 15% على هامش الربح فقط (سيارات مستعملة)
    MarginScheme = 3,     // نظام هامش الربح: يُعامَل كما في الواجهة (بدون ضريبة إضافية)
    Exempt = 4            // معفى من الضريبة
}
