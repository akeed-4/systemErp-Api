namespace ERP.Core.Models.Accounting;

/// <summary>
/// تحليل تكلفة سطر في جدول الكميات (دراسة الجدوى) مجمّداً وقت التسعير: لكل مورد من مكتبة المقاولات (بعد فك المركّب) كميته على
/// السطر وتكلفة وحدته من سجل مكتبة التكاليف (<see cref="CostId"/> وتاريخ سريانه) ومدخلات الحساب. تغيّر المكتبة لا يغيّره؛
/// تحديثه إجراء صريح يعرض الفروق قبل التطبيق. تكلفة وحدة السطر التقديرية = Σ (الكمية × تكلفة الوحدة) ÷ كمية السطر وقت التسعير.
/// مرجع السطر والمورد بلا قيد (أسطر العقد تُستبدل في مسودته)، وتُحذف مع سطرها أو عقدها.
/// </summary>
public class ContractItemResourceCost : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid ContractItemId { get; set; }
    /// <summary>بند المكتبة وبنده الفرعي اللذان حُلِّل منهما السطر: تغيّر ربط السطر يُسقط التحليل.</summary>
    public Guid? WorkItemId { get; set; }
    public Guid? WorkSubItemId { get; set; }

    public Guid ResourceId { get; set; }
    /// <summary><see cref="BoqResourceTypes"/> وقت التسعير.</summary>
    public string ResourceType { get; set; } = BoqResourceTypes.Material;
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    /// <summary>وحدة المورد.</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>ساعات العمل في وحدة المورد (للعمالة والمعدات: الساعات المخططة = الكمية × هذه).</summary>
    public decimal HoursPerUnit { get; set; } = 1m;

    /// <summary>كمية السطر (المعدّلة) وقت التسعير.</summary>
    public decimal BaseQuantity { get; set; }
    /// <summary>كمية المورد على السطر بوحدته (المعامل × كمية المالك × (1 + الهدر)، أو الثابتة).</summary>
    public decimal Quantity { get; set; }
    /// <summary>لكل وحدة من السطر = <see cref="Quantity"/> ÷ <see cref="BaseQuantity"/>.</summary>
    public decimal QuantityPerUnit { get; set; }

    /// <summary>تكلفة الوحدة من المكتبة في تاريخ التسعير.</summary>
    public decimal StandardUnitCost { get; set; }
    /// <summary>تكلفة الوحدة المعتمدة في الدراسة (المعيارية، أو المعدّلة يدوياً).</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>سطر سجل التكلفة الذي جاءت منه المعيارية (فارغ لمورد بلا سجل).</summary>
    public Guid? CostId { get; set; }
    public DateTime? CostEffectiveFrom { get; set; }
    /// <summary>مصدر تكلفة المورد في المكتبة وقت التسعير.</summary>
    public string CostSource { get; set; } = ResourceCostSources.Manual;
    /// <summary><see cref="ContractItemCostStatuses"/>.</summary>
    public string CostStatus { get; set; } = ContractItemCostStatuses.Standard;
    public string? OverrideReason { get; set; }

    /// <summary>منشأ الكمية من المكتبة (البند الفرعي والمركّب والمعامل والهدر وكمية المالك لكل جزء)، JSON.</summary>
    public string? InputsJson { get; set; }
    /// <summary>تاريخ أسعار المكتبة المستخدم.</summary>
    public DateTime PricingDate { get; set; }
    public DateTime PricedAt { get; set; }
    public string? PricedBy { get; set; }
    public Guid? PricedByUserId { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>حالة تكلفة المورد في الدراسة.</summary>
public static class ContractItemCostStatuses
{
    /// <summary>تكلفة المكتبة كما هي عند التسعير الأول.</summary>
    public const string Standard = "standard";
    /// <summary>عُدّلت يدوياً بسبب (بصلاحية).</summary>
    public const string Overridden = "overridden";
    /// <summary>حُدّثت من المكتبة بإجراء صريح وتغيّرت قيمتها.</summary>
    public const string Updated = "updated";
}
