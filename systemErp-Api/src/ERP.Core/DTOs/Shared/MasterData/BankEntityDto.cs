namespace ERP.Core.DTOs.Shared;

public partial class BankEntityDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string SwiftCode { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    /// <summary>رمز الدولة ISO 3166-1 alpha-2 (SA افتراضياً): أول حقول العنوان الوطني المطلوب للهيئة.</summary>
    public string Country { get; set; } = "SA";
    public string? City { get; set; }
    public string? District { get; set; }
    public string? Street { get; set; }
    public string? BuildingNo { get; set; }
    public string? PostalCode { get; set; }
    public string? AdditionalNo { get; set; }
    public string Currency { get; set; } = "SAR";
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string? AccountCode { get; set; }
    public string Status { get; set; } = "active";
    public string? Notes { get; set; }
}
