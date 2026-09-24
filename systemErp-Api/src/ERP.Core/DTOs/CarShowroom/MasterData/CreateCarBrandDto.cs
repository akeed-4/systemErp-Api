namespace ERP.Core.DTOs.CarShowroom;

public partial class CreateCarBrandDto
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
}
