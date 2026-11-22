namespace ERP.Core.DTOs.Accounting.Contracts;

public class StockReservationDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Notes { get; set; }
    /// <summary>طلب تحويل معلّق من هذا الحجز (كميته محجوبة عن تحويل آخر).</summary>
    public decimal PendingTransferQuantity { get; set; }
}

public class SaveStockReservationDto
{
    public Guid ProductId { get; set; }
    /// <summary>بلا قيمة: مستودع المشروع، وإلا الافتراضي.</summary>
    public Guid? WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class ReleaseStockReservationDto
{
    /// <summary>بلا قيمة: كل المتبقي.</summary>
    public decimal? Quantity { get; set; }
    public string? Reason { get; set; }
}

public class RequestReservationTransferDto
{
    public Guid TargetContractId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ReservationTransferDecisionDto
{
    public string? Note { get; set; }
}

public class StockReservationTransferDto
{
    public Guid Id { get; set; }
    public Guid SourceReservationId { get; set; }
    public Guid SourceContractId { get; set; }
    public string? SourceContractNumber { get; set; }
    public Guid TargetContractId { get; set; }
    public string? TargetContractNumber { get; set; }
    public Guid ProductId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? RequestedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>
/// أرقام صنف في مستودع (أو كل المستودعات): الرصيد الفعلي، والمحجوز للمشاريع (المتبقي من الحجوزات النشطة)، والمصروف للمشاريع
/// (صافي أذونات التسليم المرحّلة)، والمتاح فعلاً = الرصيد − المحجوز، وقيد التوريد (طلبات شراء معتمدة لم تُستلم بعد + فواتير شراء
/// محوّلة منها لم تُرحَّل). مع المشروع: المحجوز له والمتاح له (المتاح العام + حجزه).
/// </summary>
public class StockAvailabilityDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }
    public decimal Issued { get; set; }
    public decimal Available { get; set; }
    public decimal Incoming { get; set; }
    public decimal? ReservedForProject { get; set; }
    public decimal? AvailableForProject { get; set; }
}
