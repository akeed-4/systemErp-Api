namespace ERP.Core.DTOs.Shared;

public partial class CreateCustomerDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    public string? CrNumber { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Street { get; set; }
    public string? BuildingNo { get; set; }
    public string? PostalCode { get; set; }
    public string? AdditionalNo { get; set; }
    public decimal CreditLimit { get; set; }
    public int CreditPeriodDays { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string? Currency { get; set; }
    public string Status { get; set; } = "active";
    public string? Notes { get; set; }
}
