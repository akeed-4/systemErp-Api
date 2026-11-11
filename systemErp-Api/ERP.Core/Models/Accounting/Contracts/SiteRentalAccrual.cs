namespace ERP.Core.Models.Accounting;

/// <summary>
/// استحقاق إيجار آلية مستأجرة عن يوم في التقرير اليومي المعتمد (لكل تقرير وآلية): التكلفة مقيّدة على المشروع قبل وصول فاتورة المؤجر
/// (مدين تكلفة معدات مستأجرة للمشاريع بمركز المشروع / دائن إيجار معدات مستحق). فاتورة المؤجر تُرحَّل كما هي (فاتورة ضريبية)،
/// وتسويتها تنقل المطابَق من المستحق إلى حساب الفاتورة فلا تُحمَّل التكلفة مرتين. المفتوح = المبلغ − المسوّى − المقفل.
/// </summary>
public class SiteRentalAccrual : BaseEntity
{
    public Guid ContractId { get; set; }
    public Guid CostCenterId { get; set; }
    public Guid ReportId { get; set; }
    public DateTime ReportDate { get; set; }
    public Guid ProjectResourceId { get; set; }
    /// <summary>المؤجر (مورد الآلية).</summary>
    public Guid SupplierId { get; set; }
    /// <summary>المستحق الحالي بعد تعديلات إعادة الاعتماد.</summary>
    public decimal Amount { get; set; }
    /// <summary>ما طوبق مع فواتير المؤجر.</summary>
    public decimal SettledAmount { get; set; }
    /// <summary>ما أُقفل يدوياً (فوترة المؤجر أقل من المستحق).</summary>
    public decimal ClosedAmount { get; set; }

    public decimal OpenAmount => Amount - SettledAmount - ClosedAmount;
}

/// <summary>نوع حركة الاستحقاق.</summary>
public static class SiteRentalMovementKinds
{
    /// <summary>استحقاق الاعتماد الأول.</summary>
    public const string Accrual = "accrual";
    /// <summary>فرق إعادة الاعتماد بعد إعادة فتح التقرير (موجب أو سالب).</summary>
    public const string Adjustment = "adjustment";
    /// <summary>مطابقة مع فواتير المؤجر (سالبة = فك مطابقة بعد تعديل فاتورة أو حذفها أو مرتجع).</summary>
    public const string Settlement = "settlement";
    /// <summary>إقفال المتبقي حين فوترة المؤجر أقل من المستحق.</summary>
    public const string Closure = "closure";
}

/// <summary>
/// سجل حركات الاستحقاق بقيودها (لا تُعدَّل ولا تُحذف؛ التصحيح حركة جديدة): الاستحقاق وفرق إعادة الاعتماد بمرّته
/// (فريد لكل آلية ومرّة فلا يتكرر)، والمطابقة وفكها، والإقفال. مجموع المطابقة لكل مؤجر ومركز = ما سُوّي من فواتيره.
/// </summary>
public class SiteRentalAccrualMovement : BaseEntity
{
    public Guid AccrualId { get; set; }
    public Guid SupplierId { get; set; }
    public Guid CostCenterId { get; set; }
    public string Kind { get; set; } = SiteRentalMovementKinds.Accrual;
    /// <summary>بإشارته: موجب يزيد المستحق أو المسوّى أو المقفل حسب النوع.</summary>
    public decimal Amount { get; set; }
    /// <summary>مرّة ترحيل التقرير (للاستحقاق والفرق)، وصفر لغيرهما.</summary>
    public int Revision { get; set; }
    public Guid? JournalEntryId { get; set; }
    /// <summary>
    /// الحساب المقابل للمستحق في قيد الحركة: حساب مصروف فاتورة المؤجر للمطابقة (فيُعكس عليه نفسه عند فكها ولو حُذفت الفاتورة)،
    /// وحساب تكلفة المعدات المستأجرة لغيرها.
    /// </summary>
    public string? AccountCode { get; set; }
    /// <summary>المستند الذي سبّب الحركة: التقرير، أو فاتورة المؤجر، أو الإقفال.</summary>
    public Guid? SourceDocumentId { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
}
