namespace ERP.Core.Models.Shared;

/// <summary>سياسة تكلفة المخزون للمنشأة (CostingPolicyConfig في الواجهة). سجل واحد لكل منشأة.</summary>
public class CostingPolicy : BaseEntity
{
    public CostingMethod Method { get; set; } = CostingMethod.MovingAverage;
    public bool RecalculateOnNewPurchase { get; set; } = true;
    public bool IncludeFreightAndCustoms { get; set; }
    /// <summary>prohibit | allow_with_last_cost</summary>
    public string NegativeInventoryPolicy { get; set; } = "prohibit";
    public string StandardCostVarianceAccountCode { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
