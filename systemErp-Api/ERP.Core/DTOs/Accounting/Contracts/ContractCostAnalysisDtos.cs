namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>تحليل تكلفة جدول الكميات (دراسة الجدوى) المجمّد: لكل سطر موارده بتكلفتها، وإجماليات الأنواع.</summary>
public class ContractCostAnalysisDto
{
    public Guid ContractId { get; set; }
    /// <summary>التسعير والتحديث والتعديل متاحة (مسودة)؛ وإلا للقراءة.</summary>
    public bool Editable { get; set; }
    public decimal EstimatedCost { get; set; }
    /// <summary>من أسطر مسعّرة من المكتبة (والباقي تكلفته التقديرية اليدوية).</summary>
    public decimal AnalyzedCost { get; set; }
    public List<CostTypeTotalDto> TotalsByType { get; set; } = new();
    public List<ContractCostLineDto> Lines { get; set; } = new();
    public string Currency { get; set; } = string.Empty;
}

public class CostTypeTotalDto
{
    public string ResourceType { get; set; } = string.Empty;
    public decimal Total { get; set; }
    /// <summary>ساعات العمالة أو المعدات المخططة (الكمية × ساعات الوحدة).</summary>
    public decimal Hours { get; set; }
}

public class ContractCostLineDto
{
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public bool IsSubItem { get; set; }
    public bool IsGroup { get; set; }
    /// <summary>priced = له تحليل مجمّد، manual = تكلفته يدوية (غير مسعّر من المكتبة)، skipped = لا يمكن تحليله، group = مجموعة.</summary>
    public string Status { get; set; } = "manual";
    /// <summary>سبب التخطي (<c>ProjectMaterialDiagnostics</c>).</summary>
    public string? SkipReason { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public decimal EstimatedTotal { get; set; }
    public DateTime? PricingDate { get; set; }
    public List<ContractCostResourceDto> Resources { get; set; } = new();
}

public class ContractCostResourceDto
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal QuantityPerUnit { get; set; }
    public decimal Hours { get; set; }
    public decimal StandardUnitCost { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime? CostEffectiveFrom { get; set; }
    public string CostSource { get; set; } = string.Empty;
    public string CostStatus { get; set; } = string.Empty;
    public string? OverrideReason { get; set; }
    /// <summary>منشأ الكمية من المكتبة (للتتبع).</summary>
    public List<ContractCostInputDto> Inputs { get; set; } = new();
}

/// <summary>جزء من كمية المورد: البند الفرعي والمركّب في المكتبة، والمعامل أو الثابتة، والهدر، وكمية المالك.</summary>
public class ContractCostInputDto
{
    public string? LibrarySubItem { get; set; }
    public string? Composite { get; set; }
    public string QuantityMode { get; set; } = string.Empty;
    public decimal ConsumptionFactor { get; set; }
    public decimal FixedQuantity { get; set; }
    public decimal WastePercent { get; set; }
    public decimal OwnerQuantity { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>معاينة التسعير من المكتبة في تاريخ: الفروق لكل سطر قبل التطبيق (لا تُحفظ).</summary>
public class ContractCostRefreshPreviewDto
{
    public DateTime PricingDate { get; set; }
    public decimal CurrentTotal { get; set; }
    public decimal NewTotal { get; set; }
    public List<ContractCostRefreshLineDto> Lines { get; set; } = new();
}

public class ContractCostRefreshLineDto
{
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>new = يُسعَّر لأول مرة، changed = تتغير تكلفته، unchanged، skipped = لا يمكن تحليله.</summary>
    public string Status { get; set; } = "unchanged";
    public string? SkipReason { get; set; }
    public decimal CurrentUnitCost { get; set; }
    public decimal NewUnitCost { get; set; }
    public decimal CurrentTotal { get; set; }
    public decimal NewTotal { get; set; }
    public List<ContractCostRefreshChangeDto> Changes { get; set; } = new();
}

public class ContractCostRefreshChangeDto
{
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    /// <summary>added، removed، changed.</summary>
    public string Kind { get; set; } = "changed";
    public decimal OldQuantity { get; set; }
    public decimal NewQuantity { get; set; }
    public decimal OldUnitCost { get; set; }
    public decimal NewUnitCost { get; set; }
    /// <summary>تكلفة معدّلة يدوياً تبقى عند التطبيق (إن طُلب الإبقاء).</summary>
    public bool Overridden { get; set; }
}

/// <summary>تطبيق التسعير من المكتبة على أسطر (كلها إن فرغت) في تاريخ أسعار (اليوم إن فرغ).</summary>
public class ApplyContractCostRefreshDto
{
    public DateTime? PricingDate { get; set; }
    public List<Guid>? ContractItemIds { get; set; }
    /// <summary>يبقي التكلفة المعدّلة يدوياً لمورد بقي في السطر (الافتراضي).</summary>
    public bool KeepOverrides { get; set; } = true;
}

/// <summary>تعديل تكلفة وحدة مورد في الدراسة يدوياً بسبب (بصلاحية).</summary>
public class OverrideContractCostDto
{
    public decimal UnitCost { get; set; }
    public string? Reason { get; set; }
}
