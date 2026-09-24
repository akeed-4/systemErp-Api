namespace ERP.Core.DTOs.Shared;

public partial class TenantDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string CrNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = "Riyadh";
    public string Country { get; set; } = "SA";
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Currency { get; set; } = "SAR";
    public string? LogoUrl { get; set; }
    public string FinancialYearStart { get; set; } = "01-01";
    public string FinancialYearEnd { get; set; } = "12-31";
    public bool IsActive { get; set; } = true;
    public ZatcaConfigDto ZatcaConfig { get; set; } = new();
}
