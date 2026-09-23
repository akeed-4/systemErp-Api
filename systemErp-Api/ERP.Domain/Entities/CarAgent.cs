using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class CarAgent : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string CommercialRecord { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string? Brand { get; set; }
    public Guid? BrandId { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? City { get; set; }
}
