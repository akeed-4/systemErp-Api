namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>خطة تنفيذ المشروع بعناصرها المولَّدة من دراسة الجدوى، وميزانيتها، وتنبيهات النقص، وحالة اعتمادها.</summary>
public class ExecutionPlanDto
{
    public Guid ContractId { get; set; }
    public Guid? PlanId { get; set; }
    /// <summary>none (لم تُجهَّز) | draft | submitted | approved.</summary>
    public string Status { get; set; } = "none";
    public int Revision { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public string? GeneratedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ReturnNote { get; set; }
    /// <summary>study | plan_approved | contracted | فارغ (عقد مباشر).</summary>
    public string? FeasibilityStatus { get; set; }
    /// <summary>العناصر تُعدَّل وتُولَّد (مسودة).</summary>
    public bool Editable { get; set; }
    public string MaterialPlanStatus { get; set; } = "none";
    public ExecutionPlanBudgetDto Budget { get; set; } = new();
    public List<ExecutionPlanPhaseDto> Phases { get; set; } = new();
    public List<ExecutionPlanMaterialDto> Materials { get; set; } = new();
    /// <summary>المواد مجمّعة بالمادة (بلا تكرار) مع عدد البنود التي تحتاجها.</summary>
    public List<ExecutionPlanMaterialTotalDto> MaterialTotals { get; set; } = new();
    public List<ProjectResourceRequirementDto> Resources { get; set; } = new();
    /// <summary>العمالة والمعدات وغيرها مجمّعة لكل مورد عبر البنود (الساعات والكمية والتكلفة)، كإجماليات المواد.</summary>
    public List<ExecutionPlanResourceTotalDto> ResourceTotals { get; set; } = new();
    public List<ExecutionPlanWarningDto> Warnings { get; set; } = new();
    /// <summary>نسخ الأساس المعتمدة السابقة (للمقارنة).</summary>
    public List<ProjectSnapshotInfoDto> Baselines { get; set; } = new();
}

public class ExecutionPlanBudgetDto
{
    public decimal ContractValue { get; set; }
    /// <summary>الميزانية التقديرية في ملف المشروع.</summary>
    public decimal EstimatedBudget { get; set; }
    /// <summary>التكلفة التقديرية للدراسة (جدول الكميات).</summary>
    public decimal StudyCost { get; set; }
    public decimal MaterialsCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal EquipmentCost { get; set; }
    public decimal OtherCost { get; set; }
    /// <summary>مجموع ما في الخطة (المواد + العمالة + المعدات + غيرها).</summary>
    public decimal PlannedCost { get; set; }
    public decimal LaborHours { get; set; }
    public decimal EquipmentHours { get; set; }
    public decimal PlannedMargin { get; set; }
}

public class ExecutionPlanPhaseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? Description { get; set; }
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
    public bool DatesSuggested { get; set; }
    public decimal WeightPercent { get; set; }
    public decimal PlannedCost { get; set; }
    public decimal Hours { get; set; }
    public string Source { get; set; } = string.Empty;
    public Guid? SupervisorUserId { get; set; }
    public string? SupervisorName { get; set; }
    public List<Guid> PredecessorIds { get; set; } = new();
    public List<Guid> ContractItemIds { get; set; } = new();
    public int MaterialCount { get; set; }
    public int ResourceCount { get; set; }
}

public class ExecutionPlanMaterialDto
{
    public Guid Id { get; set; }
    public Guid? PhaseId { get; set; }
    public Guid? BoqItemId { get; set; }
    public Guid? SubItemId { get; set; }
    public Guid? ResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal QuantityPerUnit { get; set; }
    public decimal PlannedUnitCost { get; set; }
    public decimal PlannedCost { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class ExecutionPlanMaterialTotalDto
{
    public Guid? ResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal PlannedCost { get; set; }
    public int LineCount { get; set; }
}

/// <summary>مورد غير مادي مجمّع عبر البنود: مجموع ساعاته وكميته وتكلفته المخططة، وعدد بنوده.</summary>
public class ExecutionPlanResourceTotalDto
{
    public Guid? ResourceId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal PlannedHours { get; set; }
    public decimal PlannedCost { get; set; }
    /// <summary>عدد بنود جدول الكميات التي يحتاجها المورد.</summary>
    public int LineCount { get; set; }
}

public class ProjectResourceRequirementDto
{
    public Guid? Id { get; set; }
    public Guid? PhaseId { get; set; }
    public Guid? ContractItemId { get; set; }
    public Guid? ResourceId { get; set; }
    public string ResourceType { get; set; } = "labor";
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal HoursPerUnit { get; set; } = 1m;
    public decimal PlannedHours { get; set; }
    public decimal UnitCost { get; set; }
    public decimal PlannedCost { get; set; }
    /// <summary>owned | rented | undecided (للمعدات).</summary>
    public string? Ownership { get; set; }
    public decimal SuggestedCount { get; set; }
    public string Source { get; set; } = "manual";
    public string? Notes { get; set; }
}

/// <summary>تنبيه نقص: رمز تترجمه الواجهة، وعدد العناصر المعنية، وتفصيل اختياري.</summary>
public class ExecutionPlanWarningDto
{
    public string Code { get; set; } = string.Empty;
    public int Count { get; set; }
    public string? Detail { get; set; }
    /// <summary>القسم الذي يُعالَج فيه (estimate، setup، plan...).</summary>
    public string? Section { get; set; }
}

public class ProjectSnapshotInfoDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int Revision { get; set; }
    public decimal TotalCost { get; set; }
    public decimal ContractValue { get; set; }
    public DateTime TakenAt { get; set; }
    public string? TakenBy { get; set; }
}

/// <summary>
/// المقارنة: الدراسة الأصلية (أول نسخة مجمّدة)، والأساس المعتمد الأول، والخطة الحالية، والفعلي من المستندات المعتمدة (أذونات الصرف
/// المرحّلة ناقص المرتجع، وموارد التقارير اليومية المعتمدة) — لكل فئة تكلفة ولكل مرحلة.
/// </summary>
public class ExecutionPlanComparisonDto
{
    public Guid ContractId { get; set; }
    public int? BaselineRevision { get; set; }
    public int CurrentRevision { get; set; }
    public List<PlanComparisonRowDto> Categories { get; set; } = new();
    public List<PlanComparisonRowDto> Phases { get; set; } = new();
    public PlanComparisonRowDto Total { get; set; } = new();
}

/// <summary>صف مقارنة: الفئة (material/labor/equipment/other) أو المرحلة، بالتكلفة والساعات في كل نسخة والفعلي والفرق عن الحالي.</summary>
public class PlanComparisonRowDto
{
    public string Key { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal Study { get; set; }
    public decimal Baseline { get; set; }
    public decimal Current { get; set; }
    public decimal Actual { get; set; }
    public decimal BaselineHours { get; set; }
    public decimal CurrentHours { get; set; }
    public decimal ActualHours { get; set; }
    /// <summary>الفعلي − الحالي (موجب = تجاوز).</summary>
    public decimal Variance { get; set; }
    /// <summary>الفعلي ÷ الحالي × 100.</summary>
    public decimal ConsumedPercent { get; set; }
}

/// <summary>توليد الخطة من الدراسة: الميزانية تُملأ إن كانت صفراً، ولا تُستبدل إلا بطلب صريح.</summary>
public class GenerateExecutionPlanDto
{
    public bool OverwriteBudget { get; set; }
}

/// <summary>حفظ احتياجات العمالة والمعدات (استبدال كامل: المحفوظ بمعرّفه يُحدَّث، والجديد يُضاف، والغائب يُحذف).</summary>
public class SaveResourceRequirementsDto
{
    public List<ProjectResourceRequirementDto> Items { get; set; } = new();
}

/// <summary>إعادة الخطة المقدَّمة للمسودة بسبب.</summary>
public class ReturnExecutionPlanDto
{
    public string? Reason { get; set; }
}
