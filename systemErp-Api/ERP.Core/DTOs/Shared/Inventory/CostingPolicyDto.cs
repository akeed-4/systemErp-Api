namespace ERP.Core.DTOs.Shared;

public partial class CostingPolicyDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public CostingMethod Method { get; set; } = CostingMethod.MovingAverage;
    public bool RecalculateOnNewPurchase { get; set; } = true;
    public bool IncludeFreightAndCustoms { get; set; }
    public string NegativeInventoryPolicy { get; set; } = "prohibit";
    public string StandardCostVarianceAccountCode { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; }
    public string? Notes { get; set; }
}
