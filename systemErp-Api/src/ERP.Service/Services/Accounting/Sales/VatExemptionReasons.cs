using ERP.Core.DTOs.Accounting;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أسباب عدم خضوع السطر للنسبة الأساسية برموز هيئة الزكاة (VATEX): لكل رمز تصنيفه الضريبي.
/// السطر الصفري أو المعفى أو خارج النطاق في فاتورة مبيعات يحمل سببه، ومنه تُفصل الصادرات في الإقرار.
/// </summary>
public static class VatExemptionReasons
{
    private static readonly VatExemptionReasonDto[] Catalog =
    {
        new("VATEX-SA-32", VatCategory.ZeroRated, "تصدير السلع", "Export of goods", IsExport: true),
        new("VATEX-SA-33", VatCategory.ZeroRated, "تصدير الخدمات", "Export of services", IsExport: true),
        new("VATEX-SA-34-1", VatCategory.ZeroRated, "النقل الدولي للسلع", "International transport of goods"),
        new("VATEX-SA-34-2", VatCategory.ZeroRated, "النقل الدولي للركاب", "International transport of passengers"),
        new("VATEX-SA-35", VatCategory.ZeroRated, "الأدوية والمعدات الطبية المؤهلة", "Qualifying medicines and medical equipment"),
        new("VATEX-SA-36", VatCategory.ZeroRated, "المعادن المؤهلة", "Qualifying metals"),
        new("VATEX-SA-EDU", VatCategory.ZeroRated, "خدمات التعليم الأهلي للمواطنين", "Private education to citizens"),
        new("VATEX-SA-HEA", VatCategory.ZeroRated, "خدمات الرعاية الصحية الأهلية للمواطنين", "Private healthcare to citizens"),
        new("VATEX-SA-MLTRY", VatCategory.ZeroRated, "السلع العسكرية المؤهلة", "Qualified military goods"),
        new("VATEX-SA-29", VatCategory.Exempt, "الخدمات المالية المعفاة", "Exempt financial services"),
        new("VATEX-SA-29-7", VatCategory.Exempt, "عقود التأمين على الحياة", "Life insurance contracts"),
        new("VATEX-SA-30", VatCategory.Exempt, "المعاملات العقارية المعفاة", "Exempt real estate transactions"),
        new("VATEX-SA-OOS", VatCategory.OutOfScope, "خارج نطاق ضريبة القيمة المضافة", "Not subject to VAT"),
    };

    public static IReadOnlyList<VatExemptionReasonDto> All => Catalog;

    public static VatExemptionReasonDto? Find(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : Catalog.FirstOrDefault(r => string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>رموز التصدير (تظهر في خانة الصادرات من الإقرار).</summary>
    public static readonly string[] ExportCodes = Catalog.Where(r => r.IsExport).Select(r => r.Code).ToArray();
}
