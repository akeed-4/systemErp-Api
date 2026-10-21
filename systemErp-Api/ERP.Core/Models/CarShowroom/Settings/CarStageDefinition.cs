namespace ERP.Core.Models.CarShowroom;

/// <summary>
/// تعريف مرحلة في دورتي معارض السيارات (الشراء 7 مراحل، عقود البيع 5 مراحل) كما تعرضها الواجهة.
/// كتالوج عام لكل المنشآت يُزرع مع الترحيل؛ ترتيب المراحل وانتقالها في السرفر (ProcurementStage / SalesContractStatus).
/// </summary>
public sealed class CarStageDefinition
{
    public const string Procurement = "procurement";
    public const string SalesContract = "sales_contract";

    /// <summary>procurement | sales_contract.</summary>
    public string WorkflowType { get; set; } = string.Empty;
    /// <summary>معرّف المرحلة في الواجهة (requisition... للشراء، drafts... للعقود).</summary>
    public string Id { get; set; } = string.Empty;
    public int Number { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescAr { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    /// <summary>اسم لون Tailwind (indigo, emerald...).</summary>
    public string Color { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
