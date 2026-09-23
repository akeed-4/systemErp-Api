using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class CarBrand : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Country { get; set; }
    public string? LogoUrl { get; set; }
}
