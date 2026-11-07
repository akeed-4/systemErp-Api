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
    /// <summary>القيمة التقديرية للاحتياجات = الكمية × تكلفة الصنف المرجعية (للإرشاد والاعتماد).</summary>
    public decimal EstimatedCost { get; set; }
    public List<ProjectMaterialRequirementDto> Lines { get; set; } = new();
}

/// <summary>سطر احتياج: البند والبند الفرعي والمادة، والكمية المعتمدة، والرصيد المتاح للعرض.</summary>
public class ProjectMaterialRequirementDto
{
    public Guid? Id { get; set; }
    public Guid? BoqItemId { get; set; }
    public string? BoqItemCode { get; set; }
    public string? BoqItemDescription { get; set; }
    public Guid? SubItemId { get; set; }
    public string? SubItemCode { get; set; }
    public string? SubItemDescription { get; set; }
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public string Source { get; set; } = string.Empty;
    public decimal BaselineQuantity { get; set; }
    /// <summary>المعتمد = الأساسي + التعديلات المعتمدة.</summary>
    public decimal ApprovedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal EstimatedCost { get; set; }
    /// <summary>رصيد الصنف في كل المستودعات، وفي مستودع المشروع (للعرض؛ لا حجز في هذه المرحلة).</summary>
    public decimal OnHandQuantity { get; set; }
    public decimal ProjectWarehouseQuantity { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>حفظ مسودة الخطة: الأسطر كاملة (تحل محل السابقة).</summary>
public class SaveProjectMaterialPlanDto
{
    public string? Notes { get; set; }
    public List<SaveProjectMaterialRequirementDto> Lines { get; set; } = new();
}

public class SaveProjectMaterialRequirementDto
{
    public Guid? BoqItemId { get; set; }
    public Guid? SubItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ResourceId { get; set; }
    public string Source { get; set; } = "manual";
    public decimal BaselineQuantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>معاينة الاشتقاق من جدول الكميات (لا تُحفظ): الأسطر المقترحة وتنبيهات ما تعذّر اشتقاقه.</summary>
public class DerivedProjectMaterialsDto
{
    public List<ProjectMaterialRequirementDto> Lines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>إجراء على الخطة بسبب (الإعادة للمسودة: السبب إلزامي).</summary>
public class ProjectMaterialPlanActionDto
{
    public string? Note { get; set; }
}
