namespace ERP.Core.Models.Accounting;

/// <summary>نوع وقود مركبة العميل.</summary>
public static class ServiceVehicleFuelTypes
{
    public const string Petrol = "petrol";
    public const string Diesel = "diesel";
    public const string Hybrid = "hybrid";
    public const string Electric = "electric";
    public const string Other = "other";
    public static bool IsValid(string? v) => v is Petrol or Diesel or Hybrid or Electric or Other;
}

/// <summary>
/// مركبة عميل في سجل الورشة (D4: مستقلة عن سيارات المعرض، ويمكن ربطها بسيارة مباعة منه <see cref="ShowroomVehicleId"/>). رقم الهيكل فريد في
/// المنشأة إن وُجد، واللوحة أو الهيكل مطلوب. المالك الحالي <see cref="CustomerId"/>؛ أمر العمل يحفظ لقطة عميله (O15) فلا جدول ملكية.
/// </summary>
public class ServiceVehicle : BaseEntity
{
    public Guid CustomerId { get; set; }
    public string? Vin { get; set; }
    public string? PlateNumber { get; set; }
    public string Make { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string? EngineNumber { get; set; }
    public string FuelType { get; set; } = ServiceVehicleFuelTypes.Petrol;
    /// <summary>أعلى قراءة عدّاد مسجلة (كم)؛ عدّاد الاستقبال لا يقل عنها.</summary>
    public decimal LastOdometer { get; set; }
    public Guid? ShowroomVehicleId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// استقبال مركبات عميل في زيارة واحدة (مركبة أو أكثر)؛ لكل مركبة أمر عمل مستقل. الموقع التشغيلي موقع (D1).
/// </summary>
public class WorkshopReception : BaseEntity
{
    public string ReceptionNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// حالات أمر العمل الثابتة (القواعد مبنية عليها؛ المراحل الفرعية <see cref="WorkshopStage"/> داخلها):
/// مستلم ← فحص ← بانتظار الموافقة ← معتمد ← قيد التنفيذ ↔ متوقف ← جودة ← جاهز ← مفوتر ← مُسلَّم ← مغلق، أو ملغى.
/// </summary>
public static class WorkOrderStatuses
{
    public const string Received = "received";
    public const string Inspection = "inspection";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string InProgress = "in_progress";
    public const string Paused = "paused";
    public const string Quality = "quality";
    public const string Ready = "ready";
    public const string Invoiced = "invoiced";
    public const string Delivered = "delivered";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
        { Received, Inspection, AwaitingApproval, Approved, InProgress, Paused, Quality, Ready, Invoiced, Delivered, Closed, Cancelled };

    /// <summary>الحالات التي تكون فيها المركبة في الورشة بأمر قائم (مركبة واحدة = أمر قائم واحد).</summary>
    public static readonly string[] Open = { Received, Inspection, AwaitingApproval, Approved, InProgress, Paused, Quality, Ready, Invoiced };

    /// <summary>قبل الفوترة: يُعدَّل الأمر ويُلغى.</summary>
    public static readonly string[] Editable = { Received, Inspection, AwaitingApproval, Approved, InProgress, Paused, Quality, Ready };

    public static bool IsValid(string? v) => v != null && All.Contains(v);
}

/// <summary>أولوية أمر العمل.</summary>
public static class WorkOrderPriorities
{
    public const string Normal = "normal";
    public const string Urgent = "urgent";
    public static bool IsValid(string? v) => v is Normal or Urgent;
}

/// <summary>
/// أمر عمل مركبة واحدة من استقبال: لقطة العميل عند الاستقبال (O15)، والعدّاد والوقود والشكوى، ومستشار الخدمة والموعد، والحالة والمرحلة
/// الفرعية وسبب التوقف. الأسطر والتقديرات والتنفيذ والفوترة في مراحلها.
/// </summary>
public class WorkOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ReceptionId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public Guid VehicleId { get; set; }
    public string? PlateSnapshot { get; set; }
    public string Status { get; set; } = WorkOrderStatuses.Received;
    public Guid? StageId { get; set; }
    public Guid? PauseReasonId { get; set; }
    public string Priority { get; set; } = WorkOrderPriorities.Normal;
    public decimal OdometerIn { get; set; }
    public decimal? OdometerOut { get; set; }
    /// <summary>مستوى الوقود عند الاستقبال (نسبة 0-100).</summary>
    public decimal? FuelLevelIn { get; set; }
    public string? Complaint { get; set; }
    public string? Accessories { get; set; }
    public Guid? ServiceAdvisorUserId { get; set; }
    public string? ServiceAdvisorName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? PromisedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }

    // ---------- الفوترة والتسليم والضمان (W4) ----------
    /// <summary>أمر ضمان: مطالبة على ضمان سابق (<see cref="WarrantyId"/>).</summary>
    public bool IsWarrantyJob { get; set; }
    public Guid? WarrantyId { get; set; }
    /// <summary>فاتورة المبيعات (مرجعها <c>workshop_work_order</c>، تُلغى من الأمر وحده).</summary>
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    /// <summary>قيد نقل التكلفة من تحت التنفيذ إلى تكلفة الإيراد ومصروف الضمان عند الفوترة.</summary>
    public Guid? CostJournalEntryId { get; set; }
    public decimal CostAmount { get; set; }
    public DateTime? InvoicedAt { get; set; }
    public string? DeliveredTo { get; set; }
    public string? DeliveredBy { get; set; }
    public virtual ICollection<WorkOrderDamageMark> DamageMarks { get; set; } = new List<WorkOrderDamageMark>();
}

/// <summary>سجل انتقالات أمر العمل (الحالة والمرحلة) بسببها ومن أجراها.</summary>
public class WorkOrderStatusHistory : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public Guid? PauseReasonId { get; set; }
    public string? Reason { get; set; }
    public string? ChangedBy { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>نوع الضرر الموجود عند الاستقبال.</summary>
public static class DamageTypes
{
    public const string Scratch = "scratch";
    public const string Dent = "dent";
    public const string Crack = "crack";
    public const string Broken = "broken";
    public const string Missing = "missing";
    public const string Other = "other";
    public static bool IsValid(string? v) => v is Scratch or Dent or Crack or Broken or Missing or Other;
}

public static class DamageSeverities
{
    public const string Minor = "minor";
    public const string Moderate = "moderate";
    public const string Severe = "severe";
    public static bool IsValid(string? v) => v is Minor or Moderate or Severe;
}

/// <summary>ضرر قائم عند الاستقبال على منطقة من المركبة، وصوره مرفقات بمالك <c>work_order_damage</c>.</summary>
public class WorkOrderDamageMark : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public string Area { get; set; } = string.Empty;
    public string DamageType { get; set; } = DamageTypes.Scratch;
    public string Severity { get; set; } = DamageSeverities.Minor;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public virtual WorkOrder WorkOrder { get; set; } = null!;
}

/// <summary>أنواع مالكي مرفقات الورشة.</summary>
public static class WorkshopAttachmentOwnerTypes
{
    public const string WorkOrder = "work_order";
    public const string Damage = "work_order_damage";
    public const string InspectionResult = "work_order_inspection_result";
}
