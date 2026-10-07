namespace ERP.Core.DTOs.Accounting;

public partial class CreateAgreementDto
{
    public string AgreementNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "purchase";
    public Guid? PartyId { get; set; }
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "active";
    public string? Notes { get; set; }
    public string? ReferenceNo { get; set; }
    public string? PartyNameEn { get; set; }
    public string Currency { get; set; } = "SAR";
    public List<AgreementItemDto> Items { get; set; } = new();
    /// <summary>جدول السداد (اختياري): إن وُجد فمجموع نسبه 100%.</summary>
    public List<AgreementPaymentDto> Payments { get; set; } = new();
}
