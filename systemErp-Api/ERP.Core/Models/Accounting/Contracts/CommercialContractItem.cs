namespace ERP.Core.Models.Accounting;

/// <summary>بند توريد/خدمة ضمن العقد التجاري (مرجع لبيانات التسليم والفوترة).</summary>
public class CommercialContractItem : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid? ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal TotalWithVat { get; set; }

    // ---- جدول الكميات (عقود boq_certificate) ----
    /// <summary>رقم البند في جدول الكميات (مثل 1.2.3).</summary>
    public string? LineCode { get; set; }
    /// <summary>الباب/القسم الذي يتبعه البند (أعمال ترابية، خرسانة...).</summary>
    public string? SectionName { get; set; }
    /// <summary><see cref="ContractBoqLineKinds"/>: measured | lump_sum | provisional.</summary>
    public string LineKind { get; set; } = ContractBoqLineKinds.Measured;
    /// <summary>الكمية المعتمدة بعد الملاحق؛ تساوي <see cref="Quantity"/> حتى تُعتمد ملاحق (يضبطها الخادم).</summary>
    public decimal RevisedQuantity { get; set; }
    /// <summary>نسبة السماح بتجاوز الكمية المعتمدة في المستخلصات.</summary>
    public decimal OverrunPercent { get; set; }
    public int SortOrder { get; set; }

    /// <summary>وصف الوحدة كما يرد في جدول الكميات (مثل «عمود كامل بالقاعدة»).</summary>
    public string? UnitDescription { get; set; }
    /// <summary>
    /// البند القياسي في مكتبة المقاولات الذي نُسخ منه السطر (للتتبع والتقارير). نسخة مستقلة: تعديل المكتبة أو حذف البند منها
    /// لا يغيّر العقد، فلا قيد قاعدة بيانات عليه.
    /// </summary>
    public Guid? WorkItemId { get; set; }
    /// <summary>البند الفرعي في المكتبة الذي نُسخ منه السطر الفرعي (لاشتقاق احتياجاته)؛ نسخة مستقلة بلا قيد كـ <see cref="WorkItemId"/>.</summary>
    public Guid? WorkSubItemId { get; set; }

    /// <summary>سطر فرعي (يرسله العميل): يتبع أقرب سطر رئيسي قبله في ترتيب جدول الكميات.</summary>
    public bool IsSubItem { get; set; }
    /// <summary>يضبطه الخادم من الترتيب: السطر الرئيسي الذي يتبعه هذا السطر الفرعي (نفس العقد، بلا قيد قاعدة بيانات).</summary>
    public Guid? ParentItemId { get; set; }
    /// <summary>
    /// يضبطه الخادم: سطر رئيسي له أسطر فرعية. قيمته مجموع فروعه (سعر وحدته = المجموع ÷ كميته للعرض)، ولا يدخل قيمة العقد
    /// ولا المستخلصات؛ فروعه هي التي تُسعَّر وتُقاس.
    /// </summary>
    public bool IsGroup { get; set; }
    /// <summary>
    /// تكلفة الوحدة التقديرية (دراسة الجدوى): تُقترح من تكلفة تحليل البند في المكتبة وتُعدَّل. للمجموعة = مجموع تكلفة فروعه ÷ كميته.
    /// لا تدخل الفوترة ولا المستخلصات؛ مقارنتها بسعر البيع هامش البند المخطط.
    /// </summary>
    public decimal EstimatedUnitCost { get; set; }

    public virtual CommercialContract Contract { get; set; } = null!;
}