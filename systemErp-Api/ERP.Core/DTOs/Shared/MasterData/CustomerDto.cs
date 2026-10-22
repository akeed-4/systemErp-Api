namespace ERP.Core.DTOs.Shared;

public partial class CustomerDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? VatNumber { get; set; }
    /// <summary>
    /// مسجَّل في ضريبة القيمة المضافة (منشأة لها رقم ضريبي). يحدّد نوع فاتورة البيع في المعارض: ضريبية (B2B) للمسجَّل،
    /// ومبسطة (B2C) لغيره وهي الافتراضي. المسجَّل يلزمه رقمه الضريبي.
    /// </summary>
    public bool IsVatRegistered { get; set; }
    public string? CrNumber { get; set; }
    /// <summary>نوع الهوية الأخرى المرسلة للهيئة بدل السجل التجاري: NAT هوية وطنية، IQA إقامة، PAS جواز، GCC، TIN، MOM، MLS، SAG، OTH.</summary>
    public string? IdType { get; set; }
    /// <summary>رقم الهوية الأخرى (يقابل IdType).</summary>
    public string? IdNumber { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    /// <summary>رمز الدولة ISO 3166-1 alpha-2 (SA افتراضياً): أول حقول العنوان الوطني المطلوب للهيئة.</summary>
    public string Country { get; set; } = "SA";
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
