namespace ERP.Core.DTOs.Accounting;

public class SaveRentalHandoverLineDto
{
    public Guid ContractLineId { get; set; }
    public decimal? Meter { get; set; }
    public decimal? FuelPercent { get; set; }
    /// <summary>good | fair | damaged (فارغ = good).</summary>
    public string? Condition { get; set; }
    public string? ConditionNotes { get; set; }
    public string? Accessories { get; set; }
}

/// <summary>محضر تسليم (`delivery`) أو استلام (`return`) لأسطر من عقد واحد؛ اللحظة فارغة = الآن.</summary>
public class SaveRentalHandoverDto
{
    public Guid ContractId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public DateTime? HandoverAt { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? SiteLocation { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CounterpartyIdNumber { get; set; }
    public string? CounterpartyPhone { get; set; }
    public string? Notes { get; set; }
    public List<SaveRentalHandoverLineDto> Lines { get; set; } = new();
}

public class RentalHandoverLineDto
{
    public Guid Id { get; set; }
    public Guid ContractLineId { get; set; }
    public Guid EquipmentId { get; set; }
    public string? EquipmentCode { get; set; }
    public string? EquipmentName { get; set; }
    public decimal? Meter { get; set; }
    public decimal? FuelPercent { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? ConditionNotes { get; set; }
    public string? Accessories { get; set; }
    public Guid? CarriedFromLineId { get; set; }
    /// <summary>الاستلام: ساعات التشغيل منذ التسليم (عدّاد الاستلام − عدّاد التسليم).</summary>
    public decimal? UsedHours { get; set; }
    /// <summary>الاستلام: ساعات التأخر عن النهاية المخططة (صفر إن لم يتأخر).</summary>
    public decimal LateHours { get; set; }
    /// <summary>الاستلام: فرق الوقود عن التسليم (نقاط مئوية؛ سالب = أقل).</summary>
    public decimal? FuelDifference { get; set; }
}

public class RentalHandoverDto
{
    public Guid Id { get; set; }
    public string HandoverNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime HandoverAt { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string? SiteLocation { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CounterpartyIdNumber { get; set; }
    public string? CounterpartyPhone { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AttachmentCount { get; set; }
    public bool HasDamage { get; set; }
    public List<RentalHandoverLineDto> Lines { get; set; } = new();
}

/// <summary>
/// تجديد العقد الساري بعقد لاحق (مسودة) من نهاية أسطره المختارة (كلها إن لم تُحدَّد) حتى نهاية جديدة. الأسعار من القائمة السارية في
/// بداية التجديد، أو أسعار العقد السابق (<see cref="KeepRates"/>؛ تتطلب «خصم يدوي» إن خالفت القائمة).
/// </summary>
public class RenewRentalContractDto
{
    public DateTime NewEnd { get; set; }
    public List<Guid>? LineIds { get; set; }
    public bool KeepRates { get; set; }
    public string? Term { get; set; }
    public string? Reason { get; set; }
}
