namespace ERP.Core.Models.Accounting;

/// <summary>
/// حجز كمية صنف في موقع لمشروع (عقد مقاولات). لا حركة مخزون ولا قيد: الحجز التزام على الرصيد الفعلي يمنع صرف الكمية لمشروع آخر.
/// المتبقي = <see cref="Quantity"/> − <see cref="IssuedQuantity"/> − <see cref="ReleasedQuantity"/>؛ يستهلكه ترحيل إذن تسليم المشروع نفسه
/// من الموقع نفسه (<see cref="StockReservationConsumption"/>) ويعيده إلغاؤه، ويحرّره الإلغاء أو تحويل التخصيص المعتمد.
/// الحجز وتحويله وصرف المشروع يقفلون الصنف في الموقع (قفل تطبيقي داخل المعاملة) فلا يُحجز أو يُصرف الرصيد نفسه مرتين.
/// </summary>
public class StockReservation : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    /// <summary><see cref="StockReservationStatuses"/>: نشط ما بقي منه شيء، ومغلق حين يُستهلك أو يُحرَّر كله.</summary>
    public string Status { get; set; } = StockReservationStatuses.Active;
    /// <summary><see cref="StockReservationSources"/>: يدوي، أو ناتج تحويل تخصيص معتمد (<see cref="SourceTransferId"/>).</summary>
    public string Source { get; set; } = StockReservationSources.Manual;
    public Guid? SourceTransferId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedBy { get; set; }
    public string? Notes { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

/// <summary>ما استهلكه سطر إذن تسليم مرحّل من حجز مشروعه؛ يُعلَّم معكوساً بإلغاء الإذن فيعود للحجز.</summary>
public class StockReservationConsumption : BaseEntity
{
    public Guid ReservationId { get; set; }
    public Guid IssueId { get; set; }
    public Guid IssueLineId { get; set; }
    public decimal Quantity { get; set; }
    public bool IsReversed { get; set; }
}

/// <summary>
/// طلب تحويل جزء من حجز مشروع إلى مشروع آخر (الموقع نفسه). لا يُنفَّذ إلا باعتماد من يملك «اعتماد» على شاشة الحجوزات،
/// ولا يعتمده مقدّمه؛ الاعتماد يعيد فحص المتبقي واحتياج المشروع المستلم تحت القفل نفسه.
/// </summary>
public class StockReservationTransfer : BaseEntity
{
    public Guid SourceReservationId { get; set; }
    public Guid SourceContractId { get; set; }
    public Guid TargetContractId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = StockReservationTransferStatuses.Pending;
    public Guid? RequestedByUserId { get; set; }
    public string? RequestedBy { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>الحجز المُنشأ للمشروع المستلم عند الاعتماد.</summary>
    public Guid? TargetReservationId { get; set; }
}

public static class StockReservationStatuses
{
    public const string Active = "active";
    public const string Closed = "closed";
}

public static class StockReservationSources
{
    public const string Manual = "manual";
    public const string Transfer = "transfer";
}

public static class StockReservationTransferStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
