namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateCarTrimDto
{
    public Guid ModelId { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Transmission { get; set; }
    public string? FuelType { get; set; }
    public string? EngineSize { get; set; }
}
