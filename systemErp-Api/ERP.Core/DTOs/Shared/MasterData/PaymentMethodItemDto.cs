namespace ERP.Core.DTOs.Shared;

public partial class PaymentMethodItemDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Type { get; set; } = "cash";
    public string LinkedAccountCode { get; set; } = string.Empty;
    public string LinkedAccountName { get; set; } = string.Empty;
    public string Icon { get; set; } = "payments";
    public decimal? CommissionPercent { get; set; }
    public bool RequiresReference { get; set; }
    public string Status { get; set; } = "active";
}
