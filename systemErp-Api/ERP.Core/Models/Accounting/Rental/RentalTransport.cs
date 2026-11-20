namespace ERP.Core.Models.Accounting;

/// <summary>غرض النقل: توصيل المعدات للعميل، أو إرجاعها منه، أو نقلها بين مواقعه.</summary>
public static class RentalTransportKinds
{
    public const string Delivery = "delivery";
    public const string Return = "return";
    public const string Transfer = "transfer";
    public static bool IsValid(string? v) => v is Delivery or Return or Transfer;
}

/// <summary>النقل بوسائل المنشأة (سائق ومركبة) أو بمورد نقل خارجي.</summary>
public static class RentalTransportModes
{
    public const string Internal = "internal";
    public const string External = "external";
    public static bool IsValid(string? v) => v is Internal or External;
}

/// <summary>مجدولة ← منفَّذة، أو ملغاة بسبب.</summary>
public static class RentalTransportStatuses
{
    public const string Planned = "planned";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// مهمة نقل معدات عقد تأجير: الغرض والموعد ومن وإلى، والسائق ومركبة النقل (من سجل المعدات أو وصف حر) أو مورد النقل الخارجي، وتكلفتها
/// والمبلغ المحمّل على العميل. لا تقيّد شيئاً: تكلفة المورد من فاتورة شرائه على مركز تكلفة المعدة، وتكلفة الوسائل الداخلية للتقارير،
/// والمبلغ على العميل يُفوتر مع رسوم العقد (الفوترة). المحضر المرتبط (<see cref="HandoverId"/>) اختياري.
/// </summary>
public class RentalTransportJob : BaseEntity
{
    public string JobNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public Guid? HandoverId { get; set; }
    public string Kind { get; set; } = RentalTransportKinds.Delivery;
    public string Mode { get; set; } = RentalTransportModes.Internal;
    public string Status { get; set; } = RentalTransportStatuses.Planned;
    public DateTime ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FromLocation { get; set; }
    public string? ToLocation { get; set; }
    public Guid? DriverEmployeeId { get; set; }
    /// <summary>مركبة النقل من سجل المعدات (سطحة، شاحنة)، أو وصفها الحر في <see cref="VehicleDescription"/>.</summary>
    public Guid? TransportEquipmentId { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? SupplierId { get; set; }
    /// <summary>التكلفة (التقديرية عند الجدولة، والفعلية عند التنفيذ) قبل الضريبة.</summary>
    public decimal Cost { get; set; }
    /// <summary>المبلغ على العميل قبل الضريبة (صفر = مشمول في الإيجار).</summary>
    public decimal ChargeAmount { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CompletedBy { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public virtual ICollection<RentalTransportJobLine> Lines { get; set; } = new List<RentalTransportJobLine>();
}

/// <summary>معدة منقولة في المهمة (سطر من العقد).</summary>
public class RentalTransportJobLine : BaseEntity
{
    public Guid JobId { get; set; }
    public Guid ContractLineId { get; set; }
    public Guid EquipmentId { get; set; }
    public virtual RentalTransportJob Job { get; set; } = null!;
}
