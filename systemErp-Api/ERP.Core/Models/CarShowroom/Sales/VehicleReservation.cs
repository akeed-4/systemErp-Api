namespace ERP.Core.Models.CarShowroom;

public enum VehicleReservationStatus
{
    Active = 1,
    Released = 2,    // أُلغي الحجز
    Expired = 3,     // انتهت مدته دون عربون أو عقد
    Converted = 4    // بيعت السيارة لصاحب الحجز
}

/// <summary>
/// حجز سيارة لعميل: يمنع عرضها أو بيعها لغيره حتى تاريخ الانتهاء. حجز واحد فعّال لكل سيارة،
/// يُنشأ يدوياً أو تلقائياً من سند العربون أو اعتماد عقد البيع، ويبقى ما دام أحدهما قائماً.
/// </summary>
public class VehicleReservation : BaseEntity
{
    public Guid VehicleId { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime ReservedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public VehicleReservationStatus Status { get; set; } = VehicleReservationStatus.Active;
    /// <summary>حجز أنشأه المستخدم مباشرة (يبقى حتى يُلغى أو ينتهي ولو لم يرتبط بعربون أو عقد).</summary>
    public bool IsManual { get; set; }
    /// <summary>العقد المعتمد وسند العربون المفتوح اللذان يمسكان الحجز.</summary>
    public Guid? ContractId { get; set; }
    public Guid? DepositVoucherId { get; set; }
    public string? Notes { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
}
