namespace ERP.Core.Models.Accounting;

/// <summary>حالات أمر التغيير: مسودة ← مقدَّم ← معتمد، أو إعادة للمسودة، أو مرفوض/ملغى. المعتمد وحده يغيّر العقد.</summary>
public static class ContractVariationStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public static bool IsOpen(string status) => status is Draft or Submitted;
}

/// <summary>
/// أمر تغيير (ملحق) على عقد جدول الكميات الساري: زيادة/نقص كميات بنود قائمة، وبنود جديدة بأسعارها، وتمديد المدة.
/// التسجيل والتقديم لا يغيّران العقد؛ الاعتماد وحده يضيف الفرق إلى الكمية المعدلة للبند (<see cref="CommercialContractItem.RevisedQuantity"/>)
/// وينشئ البنود الجديدة ويعدّل قيمة العقد وتكلفته التقديرية وتاريخ نهايته، ويحفظ ما قبل الاعتماد وبعده للتتبع.
/// الكمية الأصلية للبند (<see cref="CommercialContractItem.Quantity"/>) لا تتغير، فقيمة العقد الأصلية = الحالية − مجموع المعتمد.
/// </summary>
public class ContractVariation : BaseEntity
{
    public Guid ContractId { get; set; }
    public int SequenceNo { get; set; }
    public string VariationNumber { get; set; } = string.Empty;
    public DateTime VariationDate { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>سبب التغيير ومبرره.</summary>
    public string? Reason { get; set; }
    /// <summary>مرجع الطرف الآخر (رقم خطاب أو أمر تغيير العميل/الاستشاري).</summary>
    public string? ClientReference { get; set; }
    /// <summary>تمديد المدة بالأيام (يقبل السالب للتقليص).</summary>
    public int ExtensionDays { get; set; }

    /// <summary>أثر الأمر على قيمة العقد قبل الضريبة (مجموع أسطره).</summary>
    public decimal ValueChange { get; set; }
    /// <summary>أثره على التكلفة التقديرية (مجموع الكمية × تكلفة الوحدة التقديرية).</summary>
    public decimal CostChange { get; set; }

    public string Status { get; set; } = ContractVariationStatuses.Draft;
    /// <summary>سبب الإعادة أو الرفض أو الإلغاء.</summary>
    public string? ReturnNote { get; set; }
    public string? Notes { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public Guid? DecidedByUserId { get; set; }

    // لقطة العقد لحظة الاعتماد
    public decimal? ContractValueBefore { get; set; }
    public decimal? ContractValueAfter { get; set; }
    public DateTime? EndDateBefore { get; set; }
    public DateTime? EndDateAfter { get; set; }

    public virtual ICollection<ContractVariationLine> Lines { get; set; } = new List<ContractVariationLine>();
}

/// <summary>
/// سطر أمر التغيير: على بند قائم (<see cref="ContractItemId"/>) بفرق كمية موجب أو سالب وبسعر البند نفسه،
/// أو بند جديد (بلا بند قائم) بوصفه ووحدته وسعره وكميته الموجبة، ويُسجَّل البند المنشأ عند الاعتماد في <see cref="CreatedItemId"/>.
/// </summary>
public class ContractVariationLine : BaseEntity
{
    public Guid VariationId { get; set; }
    public Guid? ContractItemId { get; set; }
    public Guid? CreatedItemId { get; set; }

    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string LineKind { get; set; } = ContractBoqLineKinds.Measured;

    /// <summary>فرق الكمية (البند القائم: موجب أو سالب؛ الجديد: موجب).</summary>
    public decimal QuantityChange { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public decimal Amount { get; set; }
    public decimal CostAmount { get; set; }

    /// <summary>للقراءة: الكمية المعدلة للبند القائم قبل الأمر وبعده (تُثبَّت عند الاعتماد).</summary>
    public decimal? QuantityBefore { get; set; }
    public decimal? QuantityAfter { get; set; }

    public int SortOrder { get; set; }
    public string? Notes { get; set; }

    public virtual ContractVariation Variation { get; set; } = null!;
}
