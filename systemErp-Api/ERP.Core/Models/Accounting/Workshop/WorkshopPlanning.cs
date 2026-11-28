using ERP.Core.Models.Shared;

namespace ERP.Core.Models.Accounting;

/// <summary>منصّة/رافعة عمل في الورشة (موقع التشغيل = المستودع، D1). تُسند إليها الأوامر والمواعيد للتخطيط.</summary>
public class WorkshopBay : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? WarehouseId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class AppointmentStatuses
{
    public const string Booked = "booked";
    public const string Confirmed = "confirmed";
    public const string Arrived = "arrived";
    public const string NoShow = "no_show";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = { Booked, Confirmed, Arrived, NoShow, Cancelled };
    public static bool IsOpen(string s) => s is Booked or Confirmed;
}

/// <summary>موعد صيانة قبل الاستقبال. الوصول يربطه بأمر العمل المستلم ولا يُنشئه بنفسه (الاستقبال يبقى مصدر الأمر).</summary>
public class WorkshopAppointment : BaseEntity
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public Guid? VehicleId { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? BayId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; } = 60;
    public string? Complaint { get; set; }
    public string Status { get; set; } = AppointmentStatuses.Booked;
    public Guid? WorkOrderId { get; set; }
    public string? CancelReason { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

public static class FollowUpKinds
{
    public const string PostDeliveryCall = "post_delivery_call";
    public const string ServiceReminder = "service_reminder";
    public const string Other = "other";

    public static readonly string[] All = { PostDeliveryCall, ServiceReminder, Other };
}

public static class FollowUpStatuses
{
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

/// <summary>متابعة ما بعد التسليم أو تذكير صيانة دورية لمركبة العميل.</summary>
public class WorkshopFollowUp : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public string Kind { get; set; } = FollowUpKinds.Other;
    public DateTime DueDate { get; set; }
    public decimal? DueOdometer { get; set; }
    public string Status { get; set; } = FollowUpStatuses.Pending;
    public string? Notes { get; set; }
    public string? Outcome { get; set; }
    public Guid? AppointmentId { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? CreatedBy { get; set; }
}
