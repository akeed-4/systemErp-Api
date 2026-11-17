namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>
/// تحليل أداء مشاريع المقاولات للإدارة في فترة: الإنتاجية من التقارير اليومية المعتمدة، والعمالة والمعدات، والمراحل والمعالم،
/// والتوقع، والموقف المالي (من تقرير التكلفة نفسه، فلا تُحسب التكلفة مرتين)، والتنبيهات بإجراءاتها المقترحة. كل الأرقام من البيانات
/// المحفوظة، والمشاريع في نطاق وصول المستخدم.
/// </summary>
public class ProjectPerformanceDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime Today { get; set; }
    /// <summary>day | week | month: وحدة نقاط الاتجاه حسب طول الفترة.</summary>
    public string Bucket { get; set; } = "day";
    /// <summary>بيانات الحضور تُعرض لمن يملك شاشة حضور فريق المشروع فقط.</summary>
    public bool AttendanceVisible { get; set; }
    public ProjectPerformanceThresholdsDto Thresholds { get; set; } = new();
    public List<ProjectPerformanceRowDto> Projects { get; set; } = new();
}

/// <summary>حدود التنبيه المطبّقة (للعرض والشرح في الواجهة).</summary>
public class ProjectPerformanceThresholdsDto
{
    public decimal LowProductivityPercent { get; set; }
    public decimal CriticalProductivityPercent { get; set; }
    public decimal SpiWarning { get; set; }
    public decimal SpiCritical { get; set; }
    public decimal CpiWarning { get; set; }
    public decimal LowUtilizationPercent { get; set; }
    public decimal LowAttendancePercent { get; set; }
    public int MissingReportDays { get; set; }
}

public class ProjectPerformanceRowDto
{
    /// <summary>ملخص المشروع نفسه في قائمة المشاريع (الإنجاز المعتمد، الفريق، الحالة، التنبيهات الأساسية).</summary>
    public ConstructionProjectSummaryDto Summary { get; set; } = new();
    public ProjectProductivityDto Productivity { get; set; } = new();
    public ProjectLaborDto Labor { get; set; } = new();
    public ProjectEquipmentDto Equipment { get; set; } = new();
    public ProjectScheduleDto Schedule { get; set; } = new();
    public ProjectFinancialDto Financial { get; set; } = new();
    public List<ProjectPerformancePointDto> Trend { get; set; } = new();
    public List<ProjectManagementAlertDto> Alerts { get; set; } = new();
    /// <summary>good | watch | risk: أشد تنبيه.</summary>
    public string Health { get; set; } = "good";
}

/// <summary>الإنتاجية في الفترة من فترات التقارير المعتمدة: المستهدف مقابل المنفَّذ بقيمة البند (لتُجمع البنود المختلفة الوحدات).</summary>
public class ProjectProductivityDto
{
    public int ReportsApproved { get; set; }
    public int ReportsPending { get; set; }
    public int ReportsDraft { get; set; }
    /// <summary>أيام الفترة الواقعة داخل مدة العقد وحتى اليوم (أيام تقويمية).</summary>
    public int PeriodDays { get; set; }
    /// <summary>منها أيام لها تقرير معتمد.</summary>
    public int ReportedDays { get; set; }
    public DateTime? LastApprovedReport { get; set; }
    /// <summary>أيام منذ آخر تقرير معتمد (أو منذ بداية العقد بلا تقرير)؛ فارغ قبل البداية.</summary>
    public int? DaysSinceLastReport { get; set; }
    public decimal TargetValue { get; set; }
    public decimal ActualValue { get; set; }
    public decimal? ProductivityPercent { get; set; }
    public decimal WorkingHours { get; set; }
    public decimal DowntimeHours { get; set; }
    public List<ProjectDowntimeDto> Downtime { get; set; } = new();
    /// <summary>البنود في الفترة، الأضعف تحقيقاً أولاً.</summary>
    public List<ProjectItemProductivityDto> Items { get; set; } = new();
}

public class ProjectDowntimeDto
{
    public string Reason { get; set; } = string.Empty;
    public decimal Hours { get; set; }
}

public class ProjectItemProductivityDto
{
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? PhaseName { get; set; }
    public decimal TargetQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public decimal? ProductivityPercent { get; set; }
    public decimal ContractQuantity { get; set; }
    /// <summary>المنفَّذ تراكمياً في التقارير المعتمدة حتى نهاية الفترة.</summary>
    public decimal ExecutedToDate { get; set; }
    public decimal RemainingQuantity { get; set; }
}

/// <summary>العمالة: ساعات التقارير المعتمدة (عمالة ومهندسون)، والحضور الميداني المسجّل على المشروع (لمن له صلاحيته).</summary>
public class ProjectLaborDto
{
    public decimal Hours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal IdleHours { get; set; }
    /// <summary>عدد (مورد × يوم) في التقارير.</summary>
    public int ManDays { get; set; }
    public decimal? UtilizationPercent { get; set; }
    public int AssignedWorkers { get; set; }
    public ProjectAttendanceSummaryDto? Attendance { get; set; }
}

public class ProjectAttendanceSummaryDto
{
    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LeaveDays { get; set; }
    public int LateMinutes { get; set; }
    /// <summary>ساعات العمل المعتمدة (سجل تحقق موقعه أو اعتُمد استثناؤه).</summary>
    public decimal ApprovedHours { get; set; }
    public int PendingExceptions { get; set; }
    public decimal? AttendancePercent { get; set; }
}

/// <summary>المعدات: ساعات التشغيل والتوقف والاستعداد من التقارير المعتمدة، والاستغلال من الوقت المسجّل ومن طاقة الدوام.</summary>
public class ProjectEquipmentDto
{
    public decimal OperatingHours { get; set; }
    public decimal IdleHours { get; set; }
    public decimal StandbyHours { get; set; }
    public int EquipmentDays { get; set; }
    public decimal? UtilizationPercent { get; set; }
    /// <summary>التشغيل ÷ (أيام وجود المعدات × ساعات الدوام).</summary>
    public decimal? CapacityUtilizationPercent { get; set; }
    public int ActiveEquipment { get; set; }
    public List<ProjectEquipmentUsageDto> Units { get; set; } = new();
}

public class ProjectEquipmentUsageDto
{
    public Guid ProjectResourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal OperatingHours { get; set; }
    public decimal IdleHours { get; set; }
    public decimal StandbyHours { get; set; }
    public int Days { get; set; }
    public decimal? UtilizationPercent { get; set; }
}

/// <summary>الجدول الزمني: المراحل بإنجازها الفعلي من المنفَّذ المعتمد، والمعالم، والتوقع.</summary>
public class ProjectScheduleDto
{
    public decimal PlannedPercent { get; set; }
    /// <summary>الإنجاز الفعلي بأوزان المراحل من المنفَّذ في التقارير المعتمدة حتى اليوم (الإنجاز المعتمد للعميل في الملخص).</summary>
    public decimal ActualPercent { get; set; }
    public decimal? Spi { get; set; }
    public DateTime? ForecastEnd { get; set; }
    /// <summary>موجب = تأخر متوقع عن نهاية العقد.</summary>
    public int? ForecastDelayDays { get; set; }
    /// <summary>rate (بمعدل الإنجاز الفعلي) | completed | insufficient (لا إنجاز أو لا مدة بعد).</summary>
    public string ForecastBasis { get; set; } = "insufficient";
    public int PhasesCompleted { get; set; }
    public int PhasesInProgress { get; set; }
    public int PhasesNotStarted { get; set; }
    public int PhasesDelayed { get; set; }
    public int PhasesOverdue { get; set; }
    public List<ProjectPhasePerformanceDto> Phases { get; set; } = new();
    public List<ProjectMilestoneStatusDto> OverdueMilestones { get; set; } = new();
}

public class ProjectPhasePerformanceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
    public decimal WeightPercent { get; set; }
    public decimal PlannedPercent { get; set; }
    public decimal? ActualPercent { get; set; }
    /// <summary>not_started | in_progress | completed | delayed | overdue</summary>
    public string Status { get; set; } = "not_started";
    public DateTime? ForecastEnd { get; set; }
    public int? DelayDays { get; set; }
    public decimal PlannedCost { get; set; }
}

public class ProjectMilestoneStatusDto
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int DaysOverdue { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>الموقف المالي من تقرير التكلفة (القيود على مركز المشروع)، وتكلفة الفترة من أسطره نفسها.</summary>
public class ProjectFinancialDto
{
    public decimal ContractValue { get; set; }
    public decimal Budget { get; set; }
    public decimal ActualCost { get; set; }
    public decimal PeriodCost { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal? BudgetUsedPercent { get; set; }
    public decimal EarnedValue { get; set; }
    /// <summary>القيمة المكتسبة − الفعلي (سالب = تجاوز).</summary>
    public decimal CostVariance { get; set; }
    public decimal? Cpi { get; set; }
    public decimal EstimateAtCompletion { get; set; }
    public decimal EstimateToComplete { get; set; }
    public decimal ForecastOverrun { get; set; }
    public decimal ForecastProfit { get; set; }
    public decimal? ForecastMarginPercent { get; set; }
    public bool MissingCostCenter { get; set; }
    public int UnpostedSiteReports { get; set; }
}

/// <summary>نقطة اتجاه (يوم أو أسبوع أو شهر): الإنتاجية وساعات الموارد وتكلفة القيود فيها، والإنجاز التراكمي في نهايتها.</summary>
public class ProjectPerformancePointDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public decimal TargetValue { get; set; }
    public decimal ActualValue { get; set; }
    public decimal LaborHours { get; set; }
    public decimal EquipmentHours { get; set; }
    public decimal DowntimeHours { get; set; }
    public decimal Cost { get; set; }
    public decimal PlannedPercent { get; set; }
    public decimal ActualPercent { get; set; }
}

/// <summary>تنبيه إداري: الرمز والإجراء المقترح رموز تترجمها الواجهة، والقيمة والحد للشرح.</summary>
public class ProjectManagementAlertDto
{
    public string Code { get; set; } = string.Empty;
    /// <summary>critical | warning | info</summary>
    public string Severity { get; set; } = "warning";
    /// <summary>schedule | cost | productivity | resources | reporting</summary>
    public string Category { get; set; } = "schedule";
    public decimal? Value { get; set; }
    public decimal? Threshold { get; set; }
    /// <summary>اسم المرحلة أو المعلم أو سبب التوقف المعني.</summary>
    public string? Reference { get; set; }
    public string Action { get; set; } = string.Empty;
    /// <summary>قسم مساحة المشروع الذي يُعالج منه.</summary>
    public string Section { get; set; } = "dashboard";
}
