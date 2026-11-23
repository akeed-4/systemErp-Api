namespace ERP.Core.Models.Accounting;

// ---------- ساعات الفنيين ----------

/// <summary>نوع سجل الفني: عمل على سطر، أو توقف بسببه.</summary>
public static class TimeLogKinds
{
    public const string Work = "work";
    public const string Idle = "idle";
    public static bool IsValid(string? v) => v is Work or Idle;
}

/// <summary>
/// سجل وقت فني على أمر العمل: عمل على سطر قابل للتنفيذ، أو توقف بسبب. سجل مفتوح واحد لكل فني (فهرس مرشَّح). عند الإغلاق تُحسب الدقائق
/// والتكلفة بلقطة التكلفة المعيارية لساعته (D13)، وتُقيَّد تكلفة العمل: مدين أعمال الورشة تحت التنفيذ / دائن العمالة المحمّلة (D6).
/// التوقف لا يُرسمل (يظهر في التقارير).
/// </summary>
public class WorkOrderTimeLog : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid? LineId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Kind { get; set; } = TimeLogKinds.Work;
    public Guid? PauseReasonId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public decimal Minutes { get; set; }
    public decimal CostPerHour { get; set; }
    public decimal Cost { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

// ---------- القطع ----------

public static class PartReservationStatuses
{
    public const string Active = "active";
    public const string Consumed = "consumed";
    public const string Released = "released";
}

/// <summary>
/// حجز قطعة لسطر أمر العمل في موقع (D5: حجز محلي للورشة بلا حركة ولا قيد). المتاح للورشة = رصيد الموقع − الحجوزات النشطة
/// السارية لأوامر أخرى. ينتهي بموعده (يُعامل غير نشط بعده)، ويُستهلك بالصرف، أو يُحرَّر يدوياً.
/// </summary>
public class WorkOrderPartReservation : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>ما استهلكه الصرف من الحجز.</summary>
    public decimal ConsumedQuantity { get; set; }
    public string Status { get; set; } = PartReservationStatuses.Active;
    public DateTime? ExpiresAt { get; set; }
    public string? CreatedBy { get; set; }
}

public static class PartIssueKinds
{
    public const string Issue = "issue";
    public const string Return = "return";
}

/// <summary>
/// صرف قطعة لسطر أمر العمل من الموقع أو إرجاعها إليه: حركة مخزون (`AdjustmentOut`/`AdjustmentIn` بمصدر <c>workshop_work_order</c>)
/// بتكلفتها الفعلية، وقيد: مدين تحت التنفيذ / دائن مخزون الموقع (والعكس للإرجاع)، ما لم يكن المخزون دورياً. الإرجاع على صرف بعينه
/// وبتكلفته وتشغيلاته.
/// </summary>
public class WorkOrderPartIssue : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Kind { get; set; } = PartIssueKinds.Issue;
    /// <summary>الصرف الذي يُرجَع منه (للإرجاع).</summary>
    public Guid? ReturnOfId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime Date { get; set; }
    public Guid? StockMovementId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

// ---------- الخدمات الخارجية ----------

public static class ExternalServiceStatuses
{
    public const string Requested = "requested";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// خدمة خارجية لسطر أمر العمل عند مورد (تشكيل، برمجة، خراطة...): التكلفة التقديرية ثم الفعلية عند الإنجاز، وفاتورة الشراء المرتبطة.
/// لا تقيّد بنفسها: تكلفة المورد تُقيَّد من فاتورة شرائه.
/// </summary>
public class WorkOrderExternalService : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid SupplierId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public string Status { get; set; } = ExternalServiceStatuses.Requested;
    public DateTime? SentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
}

// ---------- الضمان ----------

public static class WarrantyStatuses
{
    public const string Active = "active";
    public const string Claimed = "claimed";
    public const string Void = "void";
}

/// <summary>
/// ضمان سطر منفَّذ في أمر عمل مُسلَّم: يبدأ من التسليم بمدة الخدمة (أو الإعداد) وحد كيلومترات من عدّاد التسليم. المطالبة أمر عمل ضمان
/// مرتبط به ضمن المدة والحد. إلغاء التسليم غير متاح، فالضمان لا يُعدَّل بعد إنشائه إلا بإبطاله بسبب.
/// </summary>
public class WorkshopWarranty : BaseEntity
{
    public Guid WorkOrderId { get; set; }
    public Guid LineId { get; set; }
    public Guid VehicleId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? KmLimit { get; set; }
    public string Status { get; set; } = WarrantyStatuses.Active;
    public Guid? ClaimWorkOrderId { get; set; }
    public string? VoidReason { get; set; }
}
