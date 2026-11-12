namespace ERP.Core.Models.Accounting;

/// <summary>حالات خطة التنفيذ: مسودة ← مقدَّمة ← معتمدة (الإعادة من المقدَّمة للمسودة بسبب). المراجعة بعد الاعتماد مسودة جديدة برقم أعلى.</summary>
public static class ExecutionPlanStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
}

/// <summary>
/// خطة تنفيذ المشروع (واحدة لكل عقد/دراسة): الغلاف الذي تُولَّد عناصره من دراسة الجدوى المجمّدة (المراحل <see cref="ProjectPhase"/>،
/// واحتياجات المواد <see cref="ProjectMaterialRequirement"/>، واحتياجات العمالة والمعدات <see cref="ProjectResourceRequirement"/>)،
/// ويُراجع ويُعتمد. الاعتماد يجمّد نسخة أساس (<see cref="ProjectSnapshot"/>)، وأي تعديل بعده مراجعة جديدة.
/// </summary>
public class ProjectExecutionPlan : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Status { get; set; } = ExecutionPlanStatuses.Draft;
    /// <summary>رقم المراجعة (1 للخطة الأولى؛ يزيد مع كل مراجعة بعد اعتماد).</summary>
    public int Revision { get; set; } = 1;
    public DateTime? GeneratedAt { get; set; }
    public string? GeneratedBy { get; set; }
    public string? Notes { get; set; }
    public string? ReturnNote { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    /// <summary>نسخة الأساس المعتمدة الحالية.</summary>
    public Guid? BaselineSnapshotId { get; set; }
}

/// <summary>
/// احتياج مخطط من مورد غير مادي (عمالة، معدة، مقاول باطن، أخرى) لبند ومرحلة: الكمية بوحدة المورد والساعات والتكلفة من تحليل الدراسة،
/// والعدد المقترح والملكية يعدّلهما المدير. لا يُنشئ موظفاً ولا أصلاً ولا مورد مشروع؛ الفعلي يُسند من سجلاتها ويُقارن بالمورد المعياري.
/// </summary>
public class ProjectResourceRequirement : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid? PhaseId { get; set; }
    /// <summary>سطر جدول الكميات (القياس) الذي نشأ منه؛ فارغ لاحتياج عام يدوي.</summary>
    public Guid? ContractItemId { get; set; }
    /// <summary>مورد مكتبة المقاولات (المعياري).</summary>
    public Guid? ResourceId { get; set; }
    /// <summary><see cref="BoqResourceTypes"/> (غير المادة).</summary>
    public string ResourceType { get; set; } = BoqResourceTypes.Labor;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal HoursPerUnit { get; set; } = 1m;
    /// <summary>الساعات المخططة (العمالة والمعدات) = الكمية × ساعات الوحدة.</summary>
    public decimal PlannedHours { get; set; }
    public decimal UnitCost { get; set; }
    public decimal PlannedCost { get; set; }
    /// <summary>للمعدات: owned | rented | undecided (لا يُفترض توفرها لمجرد ورودها في الدراسة).</summary>
    public string? Ownership { get; set; }
    /// <summary>العدد المقترح (عمال/آليات) من الساعات وأيام المرحلة؛ يعدّله المدير.</summary>
    public decimal SuggestedCount { get; set; }
    /// <summary><see cref="PlanItemSources"/>.</summary>
    public string Source { get; set; } = PlanItemSources.Derived;
    /// <summary>سطر تحليل الدراسة المجمّد الذي نشأ منه (للتتبع).</summary>
    public Guid? SourceCostRowId { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>أنواع النسخ المرجعية المجمّدة.</summary>
public static class ProjectSnapshotKinds
{
    /// <summary>دراسة الجدوى عند اعتماد الخطة: الأسطر والأسعار والتكاليف والتحليل.</summary>
    public const string Feasibility = "feasibility";
    /// <summary>أساس خطة التنفيذ المعتمدة: المراحل والاحتياجات والميزانية.</summary>
    public const string PlanBaseline = "plan_baseline";
}

/// <summary>نسخة مرجعية مجمّدة (JSON) لا تُعدَّل: الدراسة أو أساس الخطة عند الاعتماد، برقم مراجعتها.</summary>
public class ProjectSnapshot : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Kind { get; set; } = ProjectSnapshotKinds.PlanBaseline;
    public int Revision { get; set; }
    public string Json { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public decimal ContractValue { get; set; }
    public DateTime TakenAt { get; set; }
    public string? TakenBy { get; set; }
    public Guid? TakenByUserId { get; set; }
}
