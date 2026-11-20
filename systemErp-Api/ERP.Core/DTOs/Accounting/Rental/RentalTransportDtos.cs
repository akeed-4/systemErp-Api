namespace ERP.Core.DTOs.Accounting;

public class SaveRentalTransportJobDto
{
    public Guid ContractId { get; set; }
    public Guid? HandoverId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public string? FromLocation { get; set; }
    public string? ToLocation { get; set; }
    public Guid? DriverEmployeeId { get; set; }
    public Guid? TransportEquipmentId { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal Cost { get; set; }
    public decimal ChargeAmount { get; set; }
    public string? Notes { get; set; }
    /// <summary>أسطر العقد المنقولة (معدة واحدة على الأقل).</summary>
    public List<Guid> ContractLineIds { get; set; } = new();
}

/// <summary>تنفيذ المهمة: لحظته (فارغ = الآن) وتكلفتها الفعلية (فارغ = التقديرية).</summary>
public class CompleteRentalTransportJobDto
{
    public DateTime? CompletedAt { get; set; }
    public decimal? Cost { get; set; }
    public Guid? HandoverId { get; set; }
}

public class RentalTransportJobLineDto
{
    public Guid Id { get; set; }
    public Guid ContractLineId { get; set; }
    public Guid EquipmentId { get; set; }
    public string? EquipmentCode { get; set; }
    public string? EquipmentName { get; set; }
}

public class RentalTransportJobDto
{
    public Guid Id { get; set; }
    public string JobNumber { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? CustomerName { get; set; }
    public Guid? HandoverId { get; set; }
    public string? HandoverNumber { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FromLocation { get; set; }
    public string? ToLocation { get; set; }
    public Guid? DriverEmployeeId { get; set; }
    public string? DriverName { get; set; }
    public Guid? TransportEquipmentId { get; set; }
    public string? TransportEquipmentCode { get; set; }
    public string? VehicleDescription { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public decimal Cost { get; set; }
    public decimal ChargeAmount { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CompletedBy { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<RentalTransportJobLineDto> Lines { get; set; } = new();
}

/// <summary>تعيين مشغّل سطر بمشغّل أو تغييره (فارغ = إزالته)، بسبب إن كان السطر خارجاً.</summary>
public class AssignRentalOperatorDto
{
    public Guid? OperatorEmployeeId { get; set; }
    public string? Reason { get; set; }
}
