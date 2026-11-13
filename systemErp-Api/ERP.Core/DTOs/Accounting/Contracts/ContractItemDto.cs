namespace ERP.Core.DTOs.Accounting;

public partial class ContractItemDto
{
    public Guid Id { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>وصف البند بالإنجليزية (اختياري) للطباعة باللغة الإنجليزية.</summary>
    public string? DescriptionEn { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    /// <summary>
    /// سعر الوحدة المتعاقد عليه. في جدول الكميات للقراءة: يحسبه الخادم = سعر العطاء × (1 − خصم التفاوض).
    /// عميل لا يرسل سعر العطاء يُعامل هذا السعر سعرَ عطاء.
    /// </summary>
    public decimal UnitPrice { get; set; }
    /// <summary>سعر الوحدة في العطاء قبل خصم التفاوض (جدول الكميات). صفر = يؤخذ من <see cref="UnitPrice"/>.</summary>
    public decimal TenderUnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal TotalWithVat { get; set; }
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    /// <summary>القسم بالإنجليزية (اختياري) للطباعة بالإنجليزية.</summary>
    public string? SectionNameEn { get; set; }
    public string LineKind { get; set; } = "measured";
    /// <summary>للقراءة: الكمية بعد أوامر التغيير المعتمدة (= الكمية الأصلية حتى يُعتمد أمر تغيير)؛ عليها يُقاس المستخلص.</summary>
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
    /// <summary>تكلفة الوحدة التقديرية (دراسة الجدوى)؛ للمجموعة يحسبها الخادم من فروعها.</summary>
    public decimal EstimatedUnitCost { get; set; }
    /// <summary>للقراءة: أمر التغيير الذي أضاف البند (كميته الأصلية صفر وكميته المعدلة من الأمر).</summary>
    public Guid? VariationId { get; set; }
    /// <summary>عقد مقاول الباطن: بند عقد المشروع الذي ينفذه هذا السطر (اختياري، من عقد المشروع نفسه).</summary>
    public Guid? MainContractItemId { get; set; }
}
