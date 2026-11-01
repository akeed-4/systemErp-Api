namespace ERP.Core.Models.Shared;

/// <summary>سياسة تكلفة المخزون للمنشأة (CostingPolicyConfig في الواجهة). سجل واحد لكل منشأة.</summary>
public class CostingPolicy : BaseEntity
{
    public CostingMethod Method { get; set; } = CostingMethod.MovingAverage;
    /// <summary>
    /// يحدّث فاتورة الشراء تكلفة الصنف (متوسط متحرك أو آخر شراء). عند إيقافه يبقى الصنف بتكلفته الحالية
    /// ويُرحَّل فرق سعر الشراء إلى حساب الانحرافات (StandardCostVarianceAccountCode). لا أثر له في FIFO والمعيارية.
    /// </summary>
    public bool RecalculateOnNewPurchase { get; set; } = true;
    /// <summary>
    /// اعتماد سند التكاليف الإضافية للمشتريات (شحن وتخليص) يرفع تكلفة الأصناف. عند إيقافه تُحمَّل التكاليف مصروفاً
    /// على تكلفة المبيعات (أو فرق السعر) ولا تتغيّر تكلفة الأصناف.
    /// </summary>
    public bool IncludeFreightAndCustoms { get; set; } = true;
    /// <summary>prohibit | allow_with_last_cost</summary>
    public string NegativeInventoryPolicy { get; set; } = "prohibit";
    public string StandardCostVarianceAccountCode { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
