namespace ERP.Core.DTOs.Shared;

/// <summary>فاتورة صرف أو استلام مخزني (بلا ضريبة ولا طرف).</summary>
public class InventoryVoucherDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>issue | receipt.</summary>
    public string Kind { get; set; } = "issue";
    public string VoucherNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string Status { get; set; } = "draft";
    /// <summary>سبب الحركة: internal_use | project | shortage (الصرف)، opening_balance | surplus | other_receipt (الاستلام).</summary>
    public string Reason { get; set; } = "internal_use";
    /// <summary>الحساب المستخدم في القيد (بعد الترحيل). يظهر لمن يملك عرض دليل الحسابات فقط، وإلا فارغ.</summary>
    public string? CounterAccountCode { get; set; }
    public string? CounterAccountName { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DocumentDiscount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal NetTotal { get; set; }
    public decimal InventoryValue { get; set; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public Guid? JournalEntryId { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    /// <summary>للقراءة: سند تكاليف إضافية معتمد على المستند (الاستلام) يمنع إلغاءه.</summary>
    public bool HasLandedCosts { get; set; }
    public List<InventoryVoucherLineDto> Lines { get; set; } = new();
}

public class InventoryVoucherLineDto
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid ItemId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public decimal UnitFactor { get; set; } = 1;
    public decimal Quantity { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>الحساب المقابل لقيد السطر (لمن يملك عرض دليل الحسابات).</summary>
    public string? CounterAccountCode { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal AllocatedDiscount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public Guid? ItemBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    /// <summary>للقراءة (المسودة): رصيد الصنف في موقع السطر بالوحدة الأساسية.</summary>
    public decimal? Available { get; set; }
    /// <summary>للقراءة (المسودة): تكلفة الوحدة الحالية لوحدة السطر (تقدير الصرف قبل الترحيل).</summary>
    public decimal? CurrentUnitCost { get; set; }
}

/// <summary>إنشاء/تعديل المسودة، ومع <see cref="Post"/> ترحيلها في الطلب نفسه.</summary>
public class SaveInventoryVoucherDto
{
    public DateTime? Date { get; set; }
    public Guid? WarehouseId { get; set; }
    /// <summary>سبب الحركة (فارغ = الافتراضي للنوع). الحساب المقابل منه في إعدادات الحسابات، لا يرسله المستخدم.</summary>
    public string? Reason { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    /// <summary>الاستلام فقط: خصم المستند مبلغاً.</summary>
    public decimal DocumentDiscount { get; set; }
    public string? ManualNumber { get; set; }
    public bool Post { get; set; }
    public List<SaveInventoryVoucherLineDto> Lines { get; set; } = new();
}

public class SaveInventoryVoucherLineDto
{
    public Guid ItemId { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>الاستلام: سعر الشراء لوحدة السطر. يُتجاهل في الصرف.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>الاستلام: خصم السطر مبلغاً. يُتجاهل في الصرف.</summary>
    public decimal Discount { get; set; }
    public Guid? ItemBatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ProductionDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Notes { get; set; }
}

public class CancelInventoryVoucherDto
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>أسباب الحركة لنوع المستند وطريقة الجرد؛ الحساب لكل سبب لمن يملك عرض دليل الحسابات فقط.</summary>
public class InventoryVoucherDefaultsDto
{
    public string DefaultReason { get; set; } = string.Empty;
    public bool IsPeriodic { get; set; }
    public bool CanViewAccounts { get; set; }
    public List<InventoryVoucherReasonDto> Reasons { get; set; } = new();
}

public class InventoryVoucherReasonDto
{
    public string Reason { get; set; } = string.Empty;
    /// <summary>الحساب الحالي لموقع المستند الافتراضي (التسويات بحساب موقع كل سطر)؛ فارغ لغير المخوّل.</summary>
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    /// <summary>الحساب بحسب موقع السطر (تسوية النقص والزيادة: فروقات مخزون كل موقع).</summary>
    public bool PerSite { get; set; }
}
