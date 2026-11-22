namespace ERP.Core.DTOs.Accounting;

// ---------- ساعات الفنيين ----------

/// <summary>بدء سجل (نهاية فارغة) أو تسجيله كاملاً (بداية ونهاية).</summary>
public class SaveTimeLogDto
{
    public Guid EmployeeId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? LineId { get; set; }
    public Guid? PauseReasonId { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? Notes { get; set; }
}

public class StopTimeLogDto
{
    public DateTime? EndedAt { get; set; }
    public string? Notes { get; set; }
}

public class TimeLogDto
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid? LineId { get; set; }
    public string? LineDescription { get; set; }
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? PauseReasonId { get; set; }
    public string? PauseReasonName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public decimal Minutes { get; set; }
    /// <summary>بصلاحية رؤية التكلفة فقط.</summary>
    public decimal? CostPerHour { get; set; }
    public decimal? Cost { get; set; }
    public bool Posted { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

// ---------- القطع ----------

public class ReservePartDto
{
    public Guid LineId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal Quantity { get; set; }
}

public class IssuePartDto
{
    public Guid LineId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class ReturnPartDto
{
    public Guid IssueId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class PartReservationDto
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public string? LineDescription { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    /// <summary>الحالة الفعلية: النشط بعد موعده «منتهٍ».</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PartIssueDto
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public string? LineDescription { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ReturnOfId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>بصلاحية رؤية التكلفة فقط.</summary>
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    /// <summary>للصرف: ما بقي قابلاً للإرجاع منه.</summary>
    public decimal ReturnableQuantity { get; set; }
    public DateTime Date { get; set; }
    public bool Posted { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>موقف قطعة سطر: الكمية المطلوبة والمحجوز الساري والمصروف الصافي ورصيد المستودع والمتاح للورشة.</summary>
public class PartLineStockDto
{
    public Guid LineId { get; set; }
    public string Description { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public decimal Required { get; set; }
    public decimal Reserved { get; set; }
    public decimal IssuedNet { get; set; }
    public decimal WarehouseStock { get; set; }
    /// <summary>رصيد المستودع ناقص حجوزات الأوامر الأخرى السارية.</summary>
    public decimal Available { get; set; }
    public bool CanExecute { get; set; }
}

// ---------- الخدمات الخارجية ----------

public class SaveExternalServiceDto
{
    public Guid LineId { get; set; }
    public Guid SupplierId { get; set; }
    public string? Description { get; set; }
    public decimal EstimatedCost { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Notes { get; set; }
}

public class CompleteExternalServiceDto
{
    public decimal ActualCost { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
}

public class ExternalServiceDto
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public string? LineDescription { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? PurchaseInvoiceId { get; set; }
    public string? PurchaseInvoiceNumber { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>تنفيذ أمر العمل: الساعات، وموقف القطع وحجوزاتها وصرفها، والخدمات الخارجية، والتكلفة الفعلية (بصلاحية رؤية التكلفة).</summary>
public class WorkOrderExecutionDto
{
    public Guid WorkOrderId { get; set; }
    public bool CanViewCost { get; set; }
    public List<TimeLogDto> TimeLogs { get; set; } = new();
    public List<PartLineStockDto> Parts { get; set; } = new();
    public List<PartReservationDto> Reservations { get; set; } = new();
    public List<PartIssueDto> Issues { get; set; } = new();
    public List<ExternalServiceDto> ExternalServices { get; set; } = new();
    public decimal? LaborCost { get; set; }
    public decimal? PartsCost { get; set; }
    public decimal? ExternalCost { get; set; }
    public decimal? TotalCost { get; set; }
}
