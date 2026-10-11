namespace ERP.Core.DTOs.Shared;

public partial class UnitOfMeasureDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public bool IsBaseUnit { get; set; }
    public string? BaseUnitCode { get; set; }
    public decimal ConversionFactor { get; set; } = 1.0m;
    public string Status { get; set; } = "active";
}
