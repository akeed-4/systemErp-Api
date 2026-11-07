namespace ERP.Core.Models.Accounting;

/// <summary>
/// أدوار أعضاء المشروع (عقد المقاولات هو المشروع). الدور يصف مسؤولية العضو في المشروع ويحدد من يُسند إليه المشروع؛
/// ما يستطيع فعله تحدده صلاحيات دوره في النظام.
/// </summary>
public static class ProjectMemberRoles
{
    public const string ProjectManager = "project_manager";
    /// <summary>المهندس المسؤول: يعتمد التقرير اليومي.</summary>
    public const string Engineer = "engineer";
    /// <summary>مشرف الموقع: يسجّل الإنجاز اليومي.</summary>
    public const string SiteSupervisor = "site_supervisor";
    public const string CostController = "cost_controller";
    public const string Procurement = "procurement";
    public const string Storekeeper = "storekeeper";
    public const string Finance = "finance";

    public static readonly string[] All = { ProjectManager, Engineer, SiteSupervisor, CostController, Procurement, Storekeeper, Finance };
    public static bool IsValid(string? role) => role != null && All.Contains(role);
}

/// <summary>
/// ملف مشروع المقاولات (واحد لكل عقد بجدول كميات): الميزانية التقديرية وساعات العمل التي تُولَّد منها فترات الإنجاز (ساعة لكل فترة).
/// يُنشأ مع العقد، ومعه مركز تكلفة المشروع إن لم يُختر مركز. لوحة المشروع عرض محسوب من بيانات العقد وليست سجلاً.
/// </summary>
public class ConstructionProjectProfile : BaseEntity
{
    public Guid ContractId { get; set; }
    public decimal EstimatedBudget { get; set; }
    /// <summary>بداية يوم العمل بالدقائق من منتصف الليل (420 = 07:00).</summary>
    public int WorkdayStartMinutes { get; set; } = 420;
    /// <summary>نهاية يوم العمل بالدقائق (1020 = 17:00).</summary>
    public int WorkdayEndMinutes { get; set; } = 1020;
    /// <summary>استراحة اختيارية لا تُولَّد لها فترة إنجاز.</summary>
    public int? BreakStartMinutes { get; set; }
    public int? BreakEndMinutes { get; set; }
    public string? Notes { get; set; }
}

/// <summary>مرحلة تنفيذ بتاريخيها المخططين ووزنها من المشروع؛ منها تُحسب نسبة الإنجاز المخططة في أي يوم.</summary>
public class ProjectPhase : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
    /// <summary>وزن المرحلة من المشروع (مجموع المراحل ≤ 100).</summary>
    public decimal WeightPercent { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<ProjectPhaseItem> Items { get; set; } = new List<ProjectPhaseItem>();
}

/// <summary>بند من جدول الكميات في مرحلة (البند في مرحلة واحدة). مرجع سطر العقد بلا قيد: أسطر العقد تُستبدل في مسودته.</summary>
public class ProjectPhaseItem : BaseEntity
{
    public Guid PhaseId { get; set; }
    public Guid ContractItemId { get; set; }

    public virtual ProjectPhase Phase { get; set; } = null!;
}

/// <summary>
/// عضو في المشروع بدوره. المستخدم الذي نطاق دوره «المسندة إليه» يرى المشاريع التي هو عضو فيها (إضافة لما أنشأه أو أُسند إليه)،
/// ونطاق «الكل» أو الصلاحية الكاملة لا يتقيّد بالعضوية.
/// </summary>
public class ProjectMember : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = ProjectMemberRoles.SiteSupervisor;
    public string? Notes { get; set; }
}

/// <summary>تنبيهات ملخص المشروع (رموز تترجمها الواجهة).</summary>
public static class ConstructionProjectAlerts
{
    /// <summary>لا مراحل تنفيذ: لا إنجاز مخطط.</summary>
    public const string NoPhases = "no_phases";
    /// <summary>لا خطة احتياجات، أو لم تُعتمد بعد.</summary>
    public const string MaterialsNotApproved = "materials_not_approved";
    /// <summary>احتياجات موادها غير مربوطة بالمخزون.</summary>
    public const string MaterialsNotStocked = "materials_not_stocked";
    /// <summary>تقارير يومية بانتظار الاعتماد.</summary>
    public const string ReportsPending = "reports_pending";
    /// <summary>الإنجاز المعتمد متأخر عن المخطط بأكثر من الهامش.</summary>
    public const string BehindPlan = "behind_plan";
    /// <summary>التكلفة التشغيلية تجاوزت الميزانية التقديرية.</summary>
    public const string OverBudget = "over_budget";
    /// <summary>انتهت مدة العقد والمشروع لم يُستلم.</summary>
    public const string Overdue = "overdue";
}
