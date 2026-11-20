namespace ERP.Core.DTOs.Accounting;

public class SaveRentalContractLineDto
{
    public Guid EquipmentId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Basis { get; set; } = string.Empty;
    public bool WithOperator { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    /// <summary>سعر يدوي للأساس بدل سعر القائمة (يتطلب «خصم يدوي»)؛ فارغ = سعر القائمة.</summary>
    public decimal? Rate { get; set; }
    public string? Notes { get; set; }
}

public class SaveRentalContractDto
{
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string Term { get; set; } = string.Empty;
    /// <summary>فارغ = الافتراضي للمدة (الطويل شهري، القصير عند نهاية العقد).</summary>
    public string? BillingCycle { get; set; }
    /// <summary>فارغ = الافتراضي للمدة (الطويل مقدماً، القصير مؤخراً).</summary>
    public bool? BillInAdvance { get; set; }
    public decimal DepositRequired { get; set; }
    public string? Terms { get; set; }
    public string? Notes { get; set; }
    public List<SaveRentalContractLineDto> Lines { get; set; } = new();
}

public class RentalContractLineDto
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public string? EquipmentCode { get; set; }
    public string? EquipmentName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime PlannedFrom { get; set; }
    public DateTime PlannedTo { get; set; }
    public DateTime? ActualOut { get; set; }
    public DateTime? ActualIn { get; set; }
    public decimal? MeterOut { get; set; }
    public decimal? MeterIn { get; set; }
    public string Basis { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public bool RateOverridden { get; set; }
    public bool WithOperator { get; set; }
    public decimal OperatorRate { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public string? OperatorName { get; set; }
    public decimal IncludedHours { get; set; }
    public decimal OvertimeRate { get; set; }
    public decimal MinimumPeriods { get; set; }
    public decimal EstimatedAmount { get; set; }
    public DateTime? BilledThrough { get; set; }
    public string? Notes { get; set; }
    /// <summary>خارج وتجاوز نهايته المخططة دون استلام.</summary>
    public bool IsOverdue { get; set; }
}

public class RentalContractChangeDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? LineId { get; set; }
    public DateTime? OldEnd { get; set; }
    public DateTime? NewEnd { get; set; }
    public Guid? NewContractId { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RentalContractDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid? BookingId { get; set; }
    public string? BookingNumber { get; set; }
    public Guid? ParentContractId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public bool BillInAdvance { get; set; }
    public decimal DepositRequired { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Terms { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public string? ActivatedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal EstimatedTotal { get; set; }
    public List<RentalContractLineDto> Lines { get; set; } = new();
    public List<RentalContractChangeDto> Changes { get; set; } = new();
}

/// <summary>تمديد أسطر العقد (كلها إن لم تُحدَّد) إلى نهاية جديدة بسبب.</summary>
public class ExtendRentalContractDto
{
    public DateTime NewEnd { get; set; }
    public List<Guid>? LineIds { get; set; }
    public string? Reason { get; set; }
}
