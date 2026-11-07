namespace ERP.Core.Models.Accounting;

/// <summary>طريقة تحديد كمية البند الفرعي أو المكوّن.</summary>
public static class BoqQuantityModes
{
    /// <summary>لكل وحدة من المالك: الكمية = كمية المالك × المعامل، فتتبع تغيّر كميته.</summary>
    public const string PerUnit = "per_unit";
    /// <summary>كمية ثابتة مستقلة عن كمية المالك (تجهيز موقع، تعبئة معدات...).</summary>
    public const string Fixed = "fixed";

    public static bool IsValid(string? mode) => mode is PerUnit or Fixed;
}

/// <summary>أنواع موارد تحليل الأسعار.</summary>
public static class BoqResourceTypes
{
    public const string Material = "material";
    public const string Labor = "labor";
    public const string Equipment = "equipment";
    public const string Subcontract = "subcontract";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = new[] { Material, Labor, Equipment, Subcontract, Other };
    public static bool IsValid(string? type) => type is Material or Labor or Equipment or Subcontract or Other;
}
