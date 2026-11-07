namespace ERP.Core.DTOs.Accounting;

public partial class ContractItemDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal TotalWithVat { get; set; }
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string LineKind { get; set; } = "measured";
    /// <summary>للقراءة: يضبطها الخادم (= الكمية حتى تُعتمد ملاحق).</summary>
    public decimal RevisedQuantity { get; set; }
    public decimal OverrunPercent { get; set; }
    public int SortOrder { get; set; }

    /// <summary>وصف الوحدة كما يرد في جدول الكميات.</summary>
    public string? UnitDescription { get; set; }
    /// <summary>البند القياسي في مكتبة المقاولات الذي نُسخ منه السطر (اختياري).</summary>
    public Guid? WorkItemId { get; set; }
    /// <summary>البند الفرعي في المكتبة الذي نُسخ منه السطر الفرعي (اختياري).</summary>
    public Guid? WorkSubItemId { get; set; }

    /// <summary>سطر فرعي: يتبع أقرب سطر رئيسي قبله بترتيب <see cref="SortOrder"/> (عقود جدول الكميات فقط).</summary>
    public bool IsSubItem { get; set; }
    /// <summary>للقراءة: السطر الرئيسي لهذا السطر الفرعي (يضبطه الخادم).</summary>
    public Guid? ParentItemId { get; set; }
    /// <summary>للقراءة: سطر رئيسي له فروع؛ قيمته مجموعها ولا يدخل قيمة العقد ولا المستخلصات (يضبطه الخادم).</summary>
    public bool IsGroup { get; set; }
}
