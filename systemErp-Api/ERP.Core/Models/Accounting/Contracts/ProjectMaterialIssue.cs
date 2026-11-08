namespace ERP.Core.Models.Accounting;

/// <summary>
/// صرف مواد من المستودع لمشروع (عقد مقاولات): لجهة استلام محددة، ولا يتجاوز صرف أي صنف الكمية المطلوبة في احتياجات المشروع المعتمدة
/// (دراسة الجدوى) ناقص ما سُلِّم سابقاً. الترحيل: حركة صرف بالتكلفة من المستودع، وقيد مدين تكلفة مواد المشاريع (بمركز تكلفة المشروع)
/// / دائن مخزون المستودع. الإلغاء يعكس الحركة والقيد.
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
    public string Status { get; set; } = ProjectMaterialIssueStatuses.Draft;
    public decimal TotalCost { get; set; }
    public Guid? CostCenterId { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? Notes { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? PostedBy { get; set; }
    public string? CancelReason { get; set; }

    public virtual ICollection<ProjectMaterialIssueLine> Lines { get; set; } = new List<ProjectMaterialIssueLine>();
}

public class ProjectMaterialIssueLine : BaseEntity
{
    public Guid IssueId { get; set; }
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

/// <summary>موقع توريد مواد طلب الشراء.</summary>
public static class RequisitionDeliveryLocations
{
    /// <summary>للمستودع، ثم تُصرف للمشروع بمستند صرف.</summary>
    public const string Warehouse = "warehouse";
    /// <summary>مباشرة لموقع المشروع.</summary>
    public const string ProjectSite = "project_site";

    public static bool IsValid(string? value) => value is Warehouse or ProjectSite;
}
