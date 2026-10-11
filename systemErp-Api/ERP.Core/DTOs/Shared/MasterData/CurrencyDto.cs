namespace ERP.Core.DTOs.Shared;

public partial class CurrencyDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public bool IsBaseCurrency { get; set; }
    public decimal ExchangeRate { get; set; } = 1.0m;
    public int DecimalPlaces { get; set; } = 2;
    public DateTime LastUpdated { get; set; }
    public string Status { get; set; } = "active";
}
