namespace ERP.Core.DTOs.Accounting;

public class InvoiceWorkOrderDto
{
    /// <summary>تاريخ الفاتورة (فارغ = اليوم).</summary>
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class DeliverWorkOrderDto
{
    public decimal OdometerOut { get; set; }
    public string? DeliveredTo { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>ما سيُفوتر وما سيُنقل من التكلفة قبل الإصدار (للمراجعة فقط؛ الإصدار يعيد الحساب).</summary>
public class WorkOrderBillingPreviewDto
{
    public Guid WorkOrderId { get; set; }
    public List<BillingPreviewLineDto> Lines { get; set; } = new();
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    /// <summary>أسطر لن تُفوتر (معلّقة أو مؤجلة أو مرفوضة بلا استثناء) — تنبيه.</summary>
    public int ExcludedCount { get; set; }
    /// <summary>بصلاحية رؤية التكلفة.</summary>
    public decimal? CostToRevenue { get; set; }
    public decimal? CostToWarranty { get; set; }
}

public class BillingPreviewLineDto
{
    public Guid LineId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string BillingType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal? Cost { get; set; }
}

public class WarrantyDto
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid LineId { get; set; }
    public Guid VehicleId { get; set; }
    public string? VehicleLabel { get; set; }
    public string? PlateNumber { get; set; }
    public string? CustomerName { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? KmLimit { get; set; }
    /// <summary>الحالة الفعلية: النشط بعد نهايته «منتهٍ».</summary>
    public string Status { get; set; } = string.Empty;
    public Guid? ClaimWorkOrderId { get; set; }
    public string? ClaimOrderNumber { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>مطالبة ضمان: أمر عمل ضمان للمركبة نفسها باستقبال جديد بعدّاده وشكواه.</summary>
public class ClaimWarrantyDto
{
    public decimal OdometerIn { get; set; }
    public string? Complaint { get; set; }
    public Guid? WarehouseId { get; set; }
}
