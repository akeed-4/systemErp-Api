namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// تغيير مراكز التكلفة لفاتورة بيع مرحّلة (ولو أُرسلت للهيئة): الأبعاد فقط، والمبالغ والحسابات والمخزون كما هي.
/// موقع الصرف لا يتغير بعد الترحيل (صُرف المخزون): يُعالج بتحويل مخزني.
/// </summary>
public class ReassignInvoiceCostCentersDto
{
    /// <summary>المركز الافتراضي للفاتورة (الترويسة).</summary>
    public Guid? CostCenterId { get; set; }
    /// <summary>مراكز الأسطر المراد تغييرها؛ السطر غير المذكور يبقى بمركزه.</summary>
    public List<ReassignInvoiceLineCostCenterDto> Lines { get; set; } = new();
    /// <summary>سبب التغيير (إلزامي، يُسجَّل في التدقيق).</summary>
    public string? Reason { get; set; }
}

public class ReassignInvoiceLineCostCenterDto
{
    public Guid LineId { get; set; }
    public Guid? CostCenterId { get; set; }
}
