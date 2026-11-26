namespace ERP.Core.DTOs.Shared;

public class CountryDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Code3 { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? NationalityAr { get; set; }
    public string? NationalityEn { get; set; }
    public string? DialCode { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CreateCountryDto
{
    public string Code { get; set; } = string.Empty;
    public string? Code3 { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? NationalityAr { get; set; }
    public string? NationalityEn { get; set; }
    public string? DialCode { get; set; }
    public string? CurrencyCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateCountryDto : CreateCountryDto
{
}
