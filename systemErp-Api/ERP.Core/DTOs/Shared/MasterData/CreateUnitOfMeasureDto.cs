namespace ERP.Core.DTOs.Shared;

public partial class CreateUnitOfMeasureDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    /// <summary>count | weight | volume | length (فارغ = عدد).</summary>
    public string UnitType { get; set; } = "count";
    public bool IsBaseUnit { get; set; }
    public string? BaseUnitCode { get; set; }
    public decimal ConversionFactor { get; set; } = 1.0m;
    public string Status { get; set; } = "active";
}
