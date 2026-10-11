namespace ERP.Core.Models.CarShowroom;

public class CarAgent : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string CommercialRecord { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public Guid? BrandId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
}
