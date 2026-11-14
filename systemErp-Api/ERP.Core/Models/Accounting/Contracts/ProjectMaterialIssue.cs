namespace ERP.Core.Models.Accounting;

/// <summary>
/// إذن تسليم مواد من المستودع لمشروع (عقد مقاولات): لجهة استلام محددة، وكل سطر على بند في جدول الكميات، ولا يتجاوز صرف أي صنف
/// الكمية المطلوبة في احتياجات المشروع المعتمدة (دراسة الجدوى) ناقص ما سُلِّم سابقاً. الترحيل: حركة صرف بالتكلفة الفعلية من المستودع،
/// وقيد مدين «مواد مشاريع تحت التنفيذ» (أصل، بمركز تكلفة المشروع) / دائن مخزون المستودع؛ التكلفة تُعترف عند فوترة المستخلص الذي
/// يختار هذه الكميات. الإلغاء يعكس الحركة والقيد (ويُمنع ما دامت كمياته في مستخلص غير ملغى).
/// </summary>
public class ProjectMaterialIssue : BaseEntity
{
    public Guid ContractId { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    /// <summary>المستودع المصروف منه.</summary>
    public Guid? WarehouseId { get; set; }
    /// <summary><see cref="ProjectIssueReceiverTypes"/>.</summary>
    public string ReceiverType { get; set; } = ProjectIssueReceiverTypes.SiteEngineer;
    /// <summary>اسم المستلم (المهندس، المشرف، مقاول الباطن...).</summary>
    public string ReceiverName { get; set; } = string.Empty;
    /// <summary>الموظف المستلم إن كان من الموظفين (اختياري).</summary>
    public Guid? ReceiverEmployeeId { get; set; }
    /// <summary>مقاول الباطن المستلم (مورد، اختياري).</summary>
    public Guid? ReceiverSupplierId { get; set; }
    /// <summary>طلب الشراء الذي وُرِّد للموقع ويُصرف منه (اختياري، للتتبع).</summary>
    public Guid? RequisitionId { get; set; }
    /// <summary>سند الاستلام (أو مرتجعه) الذي أنشأ هذه العهدة لنوعي الاستلام وإرجاعه.</summary>
    public Guid? DeliveryNoteId { get; set; }
    public string Status { get; set; } = ProjectMaterialIssueStatuses.Draft;
    /// <summary>نوع المستند: تسليم للموقع، أو إرجاع من الموقع للمستودع، أو هدر/تالف في الموقع (ProjectMaterialIssueKinds).</summary>
    public string Kind { get; set; } = ProjectMaterialIssueKinds.Issue;
    public decimal TotalCost { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? JournalEntryId { get; set; }
    /// <summary>
    /// الحساب المدين في قيد الترحيل: «مواد مشاريع تحت التنفيذ» وقت الترحيل، ومنه يُخفَّض عند فوترة المستخلص.
    /// فارغ = لا أصل تحت التنفيذ: مستند قديم رُحِّل مصروفاً مباشراً (517)، أو بجرد دوري بلا قيد؛ فلا يُعترف بتكلفته مرة أخرى.
    /// </summary>
    public string? DebitAccountCode { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public string? CancelReason { get; set; }

    public virtual ICollection<ProjectMaterialIssueLine> Lines { get; set; } = new List<ProjectMaterialIssueLine>();
}

public class ProjectMaterialIssueLine : BaseEntity
{
    public Guid IssueId { get; set; }
    /// <summary>بند جدول الكميات (سطر قياس غير مجموعة في العقد نفسه). فارغ في مستندات سبقت الربط بالبنود.</summary>
    public Guid? BoqItemId { get; set; }
    /// <summary>الإرجاع والهدر: سطر إذن التسليم المرحّل الذي تُسحب منه الكمية (بتكلفته الفعلية نفسها).</summary>
    public Guid? SourceLineId { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>التسليم الحالي بوحدة الصنف.</summary>
    public decimal Quantity { get; set; }
    /// <summary>تكلفة الوحدة الفعلية عند الصرف (بعد الترحيل).</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    /// <summary>رمز تزامن: اختيار الكمية في مستخلص وإلغاء المستند في الوقت نفسه لا يمرّان معاً دون اكتشاف.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ProjectMaterialIssue Issue { get; set; } = null!;
}

public static class ProjectMaterialIssueStatuses
{
    public const string Draft = "draft";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

/// <summary>جهة استلام المواد في الموقع.</summary>
public static class ProjectIssueReceiverTypes
{
    public const string SiteEngineer = "site_engineer";
    public const string Supervisor = "supervisor";
    public const string Subcontractor = "subcontractor";
    public const string Storekeeper = "site_storekeeper";
    public const string Other = "other";

    public static bool IsValid(string? type) => type is SiteEngineer or Supervisor or Subcontractor or Storekeeper or Other;
}

/// <summary>موقع توريد مواد طلب الشراء: المستودع دائماً للطلبات الجديدة (التسليم للمشروع بإذن تسليم).</summary>
public static class RequisitionDeliveryLocations
{
    /// <summary>للمستودع، ثم تُصرف للمشروع بإذن تسليم.</summary>
    public const string Warehouse = "warehouse";
    /// <summary>قيمة قديمة وصفية بلا أثر (لم تعد تُقبل في الحفظ؛ تبقى في الطلبات السابقة للعرض).</summary>
    public const string ProjectSite = "project_site";

    public static bool IsValid(string? value) => value is Warehouse or ProjectSite;
}

/// <summary>
/// أنواع مستند مواد المشروع (المستند نفسه وترقيمه ودورته): التسليم يصرف من المستودع إلى «مواد تحت التنفيذ»؛ الإرجاع يعيد كمية من
/// سطر تسليم مرحّل إلى المستودع (مدين المخزون / دائن تحت التنفيذ بتكلفة التسليم)؛ والهدر يعترف بتكلفة كمية تالفة أو مفقودة في الموقع
/// (مدين تكلفة إيرادات العقود بمركز المشروع / دائن تحت التنفيذ) بلا حركة مخزون. المسحوب من سطر بالإرجاع والهدر والمستخلصات ≤ كميته.
/// </summary>
public static class ProjectMaterialIssueKinds
{
    public const string Issue = "issue";
    public const string Return = "return";
    public const string Waste = "waste";
    /// <summary>عهدة من سند استلام مشتريات المشروع (من المورد مباشرة، لا من المستودع): ينشئها السند ويلغيها.</summary>
    public const string PurchaseReceipt = "purchase_receipt";
    /// <summary>إرجاع من عهدة الاستلام للمورد: ينشئه مرتجع السند.</summary>
    public const string ReceiptReturn = "receipt_return";

    /// <summary>ما يُنشئه المستخدم من مستندات المواد؛ الاستلام وإرجاعه ينشئهما سند الاستلام ومرتجعه.</summary>
    public static bool IsValid(string? kind) => kind is Issue or Return or Waste;
    /// <summary>تسليم للمشروع (يزيد العهدة): من المستودع أو من المورد.</summary>
    public static bool IsDelivery(string? kind) => kind is Issue or PurchaseReceipt;
    /// <summary>سحب من سطر تسليم (يُنقص العهدة).</summary>
    public static bool IsWithdrawal(string? kind) => kind is Return or Waste or ReceiptReturn;
    public static bool IsSystem(string? kind) => kind is PurchaseReceipt or ReceiptReturn;
}
