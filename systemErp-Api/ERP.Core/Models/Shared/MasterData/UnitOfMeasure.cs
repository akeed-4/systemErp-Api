namespace ERP.Core.Models.Shared;

/// <summary>
/// وحدة قياس من نوع (عدد، وزن، سائل، طول، مساحة، وقت): لكل نوع وحدة أساس واحدة، وبقية وحدات النوع تُربط بها بمعامل تحويل
/// (1 من هذه الوحدة = المعامل من وحدة الأساس).
/// </summary>
public class UnitOfMeasure : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    /// <summary>count | weight | volume | length | area | time</summary>
    public string UnitType { get; set; } = UnitTypes.Count;
    public bool IsBaseUnit { get; set; }
    public string? BaseUnitCode { get; set; }
    public decimal ConversionFactor { get; set; } = 1.0m;
    public string Status { get; set; } = "active";
}

/// <summary>أنواع وحدات القياس.</summary>
public static class UnitTypes
{
    public const string Count = "count";
    public const string Weight = "weight";
    public const string Volume = "volume";
    public const string Length = "length";
    /// <summary>مساحة (م²...): أعمال اللياسة والدهان والبلاط.</summary>
    public const string Area = "area";
    /// <summary>وقت (ساعة، يوم...): العمالة والمعدات في تحليل الأسعار.</summary>
    public const string Time = "time";
    public static readonly IReadOnlyList<string> All = new[] { Count, Weight, Volume, Length, Area, Time };
}
