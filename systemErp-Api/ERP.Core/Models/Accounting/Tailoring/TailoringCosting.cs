namespace ERP.Core.Models.Accounting;

public static class TailoringMaterialKinds
{
    public const string Issue = "issue";
    public const string Return = "return";
}

public static class TailoringLaborStatuses
{
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// صرف قماش أو مادة من المخزون لأمر (واختيارياً لقطعة) أو إرجاعها على صرف بعينه. الحركة بتكلفتها الفعلية وقيدها مدين تحت التنفيذ /
/// دائن مخزون الموقع (والإرجاع عكسه). لا يُعدَّل ولا يُحذف؛ التصحيح بالإرجاع.
/// </summary>
public class TailoringMaterialIssue : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid? GarmentId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string Kind { get; set; } = TailoringMaterialKinds.Issue;
    public Guid? ReturnOfId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public Guid? StockMovementId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>أجر قطعة لخياط عن مرحلة (آلياً عند خروج القطعة منها لأول مرة، أو يدوياً). قيده مدين تحت التنفيذ / دائن العمالة المحمّلة؛ الإلغاء بقيد عكسي.</summary>
public class TailoringLaborEntry : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid GarmentId { get; set; }
    public Guid? StageId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public bool IsManual { get; set; }
    public string Status { get; set; } = TailoringLaborStatuses.Posted;
    public string? Notes { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? ReversalEntryId { get; set; }
    public string? CancelReason { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>أجر قطعة خاص لخياط في مرحلة؛ يتقدّم على أجر المرحلة الافتراضي.</summary>
public class TailoringWorkerRate : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public Guid StageId { get; set; }
    public decimal Rate { get; set; }
}
