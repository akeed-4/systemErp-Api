using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class BankEntity : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string SwiftCode { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Currency { get; set; } = "SAR";
    public decimal OpeningBalance { get; set; }
    public decimal CurrentBalance { get; set; }
    public string? AccountCode { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }
}
