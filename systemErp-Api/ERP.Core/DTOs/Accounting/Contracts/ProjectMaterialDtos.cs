namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>خطة احتياجات مواد المشروع (العقد) بأسطرها؛ للقراءة، والأرصدة من المخزون الحالي.</summary>
public class ProjectMaterialPlanDto
{
    /// <summary>فارغ قبل أول حفظ (خطة جديدة لم تُنشأ بعد).</summary>
    public Guid? Id { get; set; }
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    /// <summary>مستودع المشروع (مستودع العقد) لعرض رصيده بجانب الرصيد الكلي.</summary>
    public Guid? WarehouseId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? ReturnNote { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    /// <summary>القيمة التقديرية للاحتياجات = الكمية × تكلفة المادة في المكتبة (للإرشاد والاعتماد).</summary>
    public decimal EstimatedCost { get; set; }
    public List<ProjectMaterialRequirementDto> Lines { get; set; } = new();
}

/// <summary>
/// سطر احتياج: البند والبند الفرعي ومادة المكتبة بكميتها بوحدتها، والصنف المخزني المربوط بالمادة (إن وُجد) بالكمية المحوّلة
/// لوحدته ورصيده للعرض. المادة غير المربوطة احتياج صحيح لا يُصرف ولا يُشترى حتى تُربط.
/// </summary>
public class ProjectMaterialRequirementDto
{
    public Guid? Id { get; set; }
    public Guid? BoqItemId { get; set; }
    public string? BoqItemCode { get; set; }
    public string? BoqItemDescription { get; set; }
    public Guid? SubItemId { get; set; }
    public string? SubItemCode { get; set; }
    public string? SubItemDescription { get; set; }
    /// <summary>مادة المكتبة (فارغة في سطر قديم سبق الربط بالمكتبة؛ تُختار مادته قبل الحفظ).</summary>
    public Guid? ResourceId { get; set; }
    public string? ResourceCode { get; set; }
    public string? ResourceName { get; set; }
    /// <summary>وحدة الكمية (وحدة المادة).</summary>
    public string Unit { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public decimal BaselineQuantity { get; set; }
    /// <summary>المعتمد = الأساسي + التعديلات المعتمدة.</summary>
    public decimal ApprovedQuantity { get; set; }
    /// <summary>تكلفة وحدة المادة الحالية في المكتبة (بوحدة السطر).</summary>
    public decimal UnitCost { get; set; }
    public decimal EstimatedCost { get; set; }

    /// <summary>المادة مربوطة بصنف مخزني موجود.</summary>
    public bool IsStockLinked { get; set; }
    public Guid? ProductId { get; set; }
    public string? Sku { get; set; }
    public string? ProductName { get; set; }
    public string? ProductUnit { get; set; }
    /// <summary>الكمية بوحدة الصنف؛ فارغة إن لم تُربط المادة أو لم تتحول وحدتها.</summary>
    public decimal? ProductQuantity { get; set; }
    /// <summary>رصيد الصنف (بوحدته) في كل المستودعات وفي مستودع المشروع؛ للعرض، لا حجز.</summary>
    public decimal? OnHandQuantity { get; set; }
    public decimal? ProjectWarehouseQuantity { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>حفظ مسودة الخطة: الأسطر كاملة (تحل محل السابقة).</summary>
public class SaveProjectMaterialPlanDto
{
    public string? Notes { get; set; }
    public List<SaveProjectMaterialRequirementDto> Lines { get; set; } = new();
}

/// <summary>سطر حفظ: مادة المكتبة إلزامية (لا اختيار مباشر لصنف مخزني؛ الصنف يُؤخذ من ربط المادة).</summary>
public class SaveProjectMaterialRequirementDto
{
    public Guid? BoqItemId { get; set; }
    public Guid? SubItemId { get; set; }
    public Guid? ResourceId { get; set; }
    public string Source { get; set; } = "manual";
    public decimal BaselineQuantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>معاينة الاشتقاق من جدول الكميات (لا تُحفظ): الأسطر المقترحة، والتنبيهات العامة، وتشخيص كل سطر في الجدول.</summary>
public class DerivedProjectMaterialsDto
{
    public List<ProjectMaterialRequirementDto> Lines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<ProjectMaterialDiagnosticDto> Diagnostics { get; set; } = new();
}

/// <summary>
/// تشخيص سطر في جدول الكميات: اشتُق (<c>derived</c>)، أو اشتُق بتنبيه (<c>warning</c>)، أو تُخطي (<c>skipped</c>) بسببه.
/// الرموز في <c>ProjectMaterialDiagnostics</c> وتترجمها الواجهة.
/// </summary>
public class ProjectMaterialDiagnosticDto
{
    /// <summary>سطر العقد نفسه (رئيسي أو فرعي).</summary>
    public Guid LineId { get; set; }
    public Guid? BoqItemId { get; set; }
    public Guid? SubItemId { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsSubItem { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public int MaterialCount { get; set; }
    /// <summary>موارد غير مادية في التحليل (عمالة ومعدات وباطن) لا تدخل الاحتياجات.</summary>
    public int OtherResourceCount { get; set; }
    public List<ProjectMaterialIssueDto> Issues { get; set; } = new();
}

/// <summary>تنبيه مادة في سطر: غير مربوطة بالمخزون، أو وحدتها لا تتحول لوحدة صنفها.</summary>
public class ProjectMaterialIssueDto
{
    public string Code { get; set; } = string.Empty;
    public Guid ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? ProductUnit { get; set; }
}

/// <summary>
/// المواد المتاحة لاختيارها على سطر في جدول الكميات: من تحليل بنده في المكتبة بكمية مقترحة من كمية العقد (<c>item</c>)،
/// أو مواد المكتبة عامة (<c>library</c>) لسطر غير مرتبط أو عند طلب الكل.
/// </summary>
public class ProjectMaterialCandidatesDto
{
    public Guid LineId { get; set; }
    public Guid? BoqItemId { get; set; }
    public Guid? SubItemId { get; set; }
    public string Scope { get; set; } = string.Empty;
    /// <summary>سبب خلو تحليل البند (رمز تشخيص) إن لم تكن له مواد في المكتبة.</summary>
    public string? Reason { get; set; }
    public List<ProjectMaterialCandidateDto> Candidates { get; set; } = new();
}

public class ProjectMaterialCandidateDto
{
    public Guid ResourceId { get; set; }
    public string ResourceCode { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public bool IsStockLinked { get; set; }
    public Guid? ProductId { get; set; }
    public string? Sku { get; set; }
    public string? ProductName { get; set; }
    public string? ProductUnit { get; set; }
    /// <summary>المادة من تحليل البند.</summary>
    public bool InItemAnalysis { get; set; }
    /// <summary>الكمية المقترحة بوحدة المادة من كمية العقد (فارغة لمادة خارج تحليل البند).</summary>
    public decimal? SuggestedQuantity { get; set; }
    public List<ProjectMaterialOriginDto> Origins { get; set; } = new();
}

/// <summary>منشأ جزء من الكمية المقترحة: البند الفرعي في المكتبة، والمورد المركّب الوسيط، والمعامل والهدر.</summary>
public class ProjectMaterialOriginDto
{
    public string? LibrarySubItem { get; set; }
    public string? Composite { get; set; }
    public string QuantityMode { get; set; } = string.Empty;
    public decimal ConsumptionFactor { get; set; }
    public decimal FixedQuantity { get; set; }
    public decimal WastePercent { get; set; }
    /// <summary>كمية مالك الاحتياج (البند أو البند الفرعي) التي ضُرب فيها المعامل.</summary>
    public decimal OwnerQuantity { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>إجراء على الخطة بسبب (الإعادة للمسودة: السبب إلزامي).</summary>
public class ProjectMaterialPlanActionDto
{
    public string? Note { get; set; }
}
