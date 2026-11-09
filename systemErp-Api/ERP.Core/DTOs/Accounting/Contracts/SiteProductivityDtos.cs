namespace ERP.Core.DTOs.Accounting.Contracts;

// ---------- موارد المشروع وتكلفة الساعة ----------

public class ProjectResourceDto
{
    public Guid? Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Category { get; set; }
    public Guid? LibraryResourceId { get; set; }
    public Guid? FixedAssetId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string RateBasis { get; set; } = "hour";
    public decimal Rate { get; set; }
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    public decimal ExtraCostPerHour { get; set; }
    public decimal HoursPerDay { get; set; } = 8;
    public decimal DaysPerMonth { get; set; } = 26;
    /// <summary>فارغ = الافتراضي حسب النوع (1 للآلية، 1.5 للمهندس والعامل).</summary>
    public decimal? OvertimeMultiplier { get; set; }
    public DateTime? RentalStart { get; set; }
    public DateTime? RentalEnd { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    /// <summary>للقراءة: محسوبة في الخادم.</summary>
    public decimal HourlyCost { get; set; }
    public decimal OvertimeHourlyCost { get; set; }
    /// <summary>للقراءة: مشاريع أخرى عليها الموظف نفسه (للمهندس والعامل).</summary>
    public List<string> OtherProjects { get; set; } = new();
}

public class ProjectDailyPlanDto
{
    public Guid? Id { get; set; }
    public Guid ContractItemId { get; set; }
    public string? ItemLabel { get; set; }
    public string? Unit { get; set; }
    public decimal TargetPerHour { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public string? Notes { get; set; }
}

/// <summary>خطة الإنجاز اليومية كاملة (تستبدل المحفوظ).</summary>
public class SaveProjectDailyPlansDto
{
    public List<ProjectDailyPlanDto> Plans { get; set; } = new();
}

// ---------- التقرير اليومي ----------

public class SiteHourEntryDto
{
    public Guid? Id { get; set; }
    /// <summary>HH:mm.</summary>
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public Guid ContractItemId { get; set; }
    public string? ItemLabel { get; set; }
    public string? Unit { get; set; }
    public decimal TargetQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public int DowntimeMinutes { get; set; }
    public string? DowntimeReason { get; set; }
    public string? Notes { get; set; }
    public decimal AchievementPercent { get; set; }
}

public class SiteResourceUsageDto
{
    public Guid? Id { get; set; }
    public Guid ProjectResourceId { get; set; }
    public string? ResourceName { get; set; }
    public string? Kind { get; set; }
    public Guid? ContractItemId { get; set; }
    public decimal Hours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal IdleHours { get; set; }
    public string? IdleReason { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal OvertimeHourlyCost { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
}

/// <summary>مادة مستهلكة على بند: من مكتبة المقاولات (إلزامية عند الحفظ)، والصنف المخزني للقراءة من ربط المادة وقت الحفظ.</summary>
public class SiteMaterialUsageDto
{
    public Guid? Id { get; set; }
    public Guid ContractItemId { get; set; }
    public Guid? ResourceId { get; set; }
    public string? ResourceCode { get; set; }
    public string? ResourceName { get; set; }
    /// <summary>للقراءة فقط.</summary>
    public Guid? ProductId { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Cost { get; set; }
    public string? Notes { get; set; }
}

/// <summary>ملخص اليوم المحسوب في الخادم (تكلفة تشغيلية غير مرحّلة).</summary>
public class SiteDailySummaryDto
{
    /// <summary>ساعات العمل = مجموع الفترات المميزة في اليوم.</summary>
    public decimal WorkingHours { get; set; }
    public decimal DowntimeHours { get; set; }
    public decimal IdleResourceHours { get; set; }
    public decimal ResourceCost { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal CostPerHour { get; set; }
    /// <summary>قيمة المستهدف والمنفَّذ بسعر البند (لمقارنة بنود بوحدات مختلفة).</summary>
    public decimal TargetValue { get; set; }
    public decimal ActualValue { get; set; }
    public decimal AchievementPercent { get; set; }
    /// <summary>تكلفة لم تُوزَّع على بند (يوم بلا إنتاج).</summary>
    public decimal UnallocatedCost { get; set; }
    public List<SiteItemSummaryDto> Items { get; set; } = new();
    public List<SiteDowntimeReasonDto> Reasons { get; set; } = new();
    /// <summary>الفترات التي قلّ إنجازها عن 90% من المستهدف (أسباب انخفاض الإنتاجية).</summary>
    public List<SiteLowSlotDto> LowSlots { get; set; } = new();
}

public class SiteItemSummaryDto
{
    public Guid ContractItemId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public decimal Actual { get; set; }
    public decimal AchievementPercent { get; set; }
    public int DowntimeMinutes { get; set; }
    public decimal Cost { get; set; }
    /// <summary>تكلفة إنتاج الوحدة = تكلفة البند في اليوم ÷ المنفَّذ.</summary>
    public decimal CostPerUnit { get; set; }
}

public class SiteDowntimeReasonDto
{
    public string Reason { get; set; } = string.Empty;
    public int Minutes { get; set; }
    public int Entries { get; set; }
}

public class SiteLowSlotDto
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public decimal Actual { get; set; }
    public decimal AchievementPercent { get; set; }
    public string? Reason { get; set; }
}

public class SiteDailyReportDto
{
    /// <summary>فارغ = تقرير جديد لم يُحفظ (قالب اليوم من الخطة).</summary>
    public Guid? Id { get; set; }
    public Guid ContractId { get; set; }
    public DateTime ReportDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ReturnNote { get; set; }
    public string? PreparedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    /// <summary>فترات ساعات العمل من إعداد المشروع (للقالب).</summary>
    public List<ProjectHourSlotDto> Slots { get; set; } = new();
    public List<SiteHourEntryDto> Hours { get; set; } = new();
    public List<SiteResourceUsageDto> Resources { get; set; } = new();
    public List<SiteMaterialUsageDto> Materials { get; set; } = new();
    public SiteDailySummaryDto Summary { get; set; } = new();
}

public class SaveSiteDailyReportDto
{
    public DateTime ReportDate { get; set; }
    public string? Notes { get; set; }
    public List<SiteHourEntryDto> Hours { get; set; } = new();
    public List<SiteResourceUsageDto> Resources { get; set; } = new();
    public List<SiteMaterialUsageDto> Materials { get; set; } = new();
}

public class SiteReportActionDto
{
    public string? Note { get; set; }
}

public class SiteReportListItemDto
{
    public Guid Id { get; set; }
    public DateTime ReportDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PreparedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public decimal AchievementPercent { get; set; }
    public decimal DowntimeHours { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>
/// مشروع يمكن للمستخدم التسجيل فيه (لقائمة اختيار المشروع في التقرير اليومي): الهوية فقط بلا أرقام مالية، كي لا يحتاج
/// المشرف صلاحية العقود أو المشاريع.
/// </summary>
public class SiteReportProjectDto
{
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
}

/// <summary>سطر قياس في جدول كميات المشروع (بلا الرئيسي ذي الفروع) لقوائم البنود في التقرير اليومي.</summary>
public class SiteReportBoqLineDto
{
    public Guid Id { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool IsSubItem { get; set; }
}

// ---------- تقارير الإنجاز ----------

/// <summary>مرشحات تقارير الإنجاز: الفترة والبند والمورد؛ المعتمد فقط افتراضاً.</summary>
public class SiteReportFilterDto
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? ContractItemId { get; set; }
    public Guid? ProjectResourceId { get; set; }
    public bool IncludeUnapproved { get; set; }
}

public class SiteHourlyRowDto
{
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public Guid ContractItemId { get; set; }
    public string ItemLabel { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public decimal Actual { get; set; }
    public decimal AchievementPercent { get; set; }
    public int DowntimeMinutes { get; set; }
    public string? DowntimeReason { get; set; }
}

public class SiteProductivityRowDto
{
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid ContractItemId { get; set; }
    public string ItemLabel { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public decimal Actual { get; set; }
    public decimal AchievementPercent { get; set; }
    public int DowntimeMinutes { get; set; }
    public decimal Cost { get; set; }
    public decimal CostPerUnit { get; set; }
}

public class SiteResourceRowDto
{
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid ProjectResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal IdleHours { get; set; }
    public decimal Cost { get; set; }
}
