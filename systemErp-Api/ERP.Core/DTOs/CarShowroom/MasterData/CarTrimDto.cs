namespace ERP.Core.DTOs.CarShowroom;

public partial class CarTrimDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid ModelId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Transmission { get; set; }
    public string? FuelType { get; set; }
    public string? EngineSize { get; set; }
}
