namespace ERP.Core.DTOs.Accounting;

// ---------- المنصات ----------

public class WorkshopBayDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class SaveWorkshopBayDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? WarehouseId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// ---------- التخطيط ----------

/// <summary>إسناد أمر العمل لمنصة وفني ونافذة زمنية. القيم الفارغة تمسح الإسناد.</summary>
public class PlanWorkOrderDto
{
    public Guid? BayId { get; set; }
    public Guid? AssignedTechnicianId { get; set; }
    public DateTime? PlannedStartAt { get; set; }
    public DateTime? PlannedEndAt { get; set; }
}

public class ScheduleItemDto
{
    /// <summary>order | appointment</summary>
    public string Kind { get; set; } = "order";
    public Guid Id { get; set; }
    public string? Number { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? PlateNumber { get; set; }
    public string? VehicleLabel { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Priority { get; set; }
    public Guid? BayId { get; set; }
    public Guid? TechnicianId { get; set; }
    public string? TechnicianName { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public DateTime? PromisedAt { get; set; }
    public bool IsOverdue { get; set; }
}

public class ScheduleTechnicianDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>دقائق مخططة للفني داخل الفترة.</summary>
    public int PlannedMinutes { get; set; }
    public bool Working { get; set; }
}

public class WorkshopScheduleDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<WorkshopBayDto> Bays { get; set; } = new();
    public List<ScheduleTechnicianDto> Technicians { get; set; } = new();
    public List<ScheduleItemDto> Items { get; set; } = new();
    /// <summary>أوامر قائمة بلا نافذة مخططة (لإسنادها).</summary>
    public List<ScheduleItemDto> Unplanned { get; set; } = new();
}

// ---------- المواعيد ----------

public class WorkshopAppointmentDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public Guid? VehicleId { get; set; }
    public string? VehicleLabel { get; set; }
    public string? PlateNumber { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? BayId { get; set; }
    public string? BayName { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    public string? Complaint { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? WorkOrderId { get; set; }
    public string? WorkOrderNumber { get; set; }
    public string? CancelReason { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveWorkshopAppointmentDto
{
    public Guid CustomerId { get; set; }
    public string? CustomerPhone { get; set; }
    public Guid? VehicleId { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? BayId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string? Complaint { get; set; }
    public string? Notes { get; set; }
}

/// <summary>confirmed | no_show (الإلغاء بسبب عبر مساره).</summary>
public class AppointmentStatusDto
{
    public string Status { get; set; } = string.Empty;
}

public class AppointmentArriveDto
{
    public Guid WorkOrderId { get; set; }
}

// ---------- المتابعة ----------

public class WorkshopFollowUpDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string? VehicleLabel { get; set; }
    public string? PlateNumber { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public Guid? WorkOrderId { get; set; }
    public string? WorkOrderNumber { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public decimal? DueOdometer { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? Outcome { get; set; }
    public Guid? AppointmentId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public bool IsOverdue { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveWorkshopFollowUpDto
{
    public Guid VehicleId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public string Kind { get; set; } = "other";
    public DateTime DueDate { get; set; }
    public decimal? DueOdometer { get; set; }
    public string? Notes { get; set; }
}

public class CompleteFollowUpDto
{
    public string? Outcome { get; set; }
    /// <summary>موعد ناتج عن المتابعة (اختياري).</summary>
    public Guid? AppointmentId { get; set; }
}

// ---------- تجربة الفني ----------

public class MyJobLineDto
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string ExecutionStatus { get; set; } = string.Empty;
    public bool Executable { get; set; }
    public int LoggedMinutes { get; set; }
}

public class MyJobDto
{
    public Guid WorkOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? PlateNumber { get; set; }
    public string? VehicleLabel { get; set; }
    public string? Complaint { get; set; }
    public string? BayName { get; set; }
    public DateTime? PlannedStartAt { get; set; }
    public DateTime? PlannedEndAt { get; set; }
    public DateTime? PromisedAt { get; set; }
    public bool AssignedToMe { get; set; }
    public List<MyJobLineDto> Lines { get; set; } = new();
}

public class MyJobsDto
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public TimeLogDto? OpenLog { get; set; }
    public int TodayMinutes { get; set; }
    public List<MyJobDto> Jobs { get; set; } = new();
}

/// <summary>بدء عمل الفني نفسه: kind = work بسطر عمالة، أو idle (توقف) بسبب.</summary>
public class MyJobStartDto
{
    public string Kind { get; set; } = "work";
    public Guid? LineId { get; set; }
    public Guid? PauseReasonId { get; set; }
    public string? Notes { get; set; }
}
