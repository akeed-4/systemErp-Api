namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>نوع عقد تديره المنشأة: مساره (كامل/مبسط) وطريقة فوترته وحد التبسيط بالقيمة.</summary>
public class ContractTypeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>full | simple</summary>
    public string Workflow { get; set; } = string.Empty;
    /// <summary>after_receipt | direct | boq_certificate</summary>
    public string Billing { get; set; } = string.Empty;
    public decimal? SimpleLimit { get; set; }
    public bool RequiresGuarantee { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    /// <summary>عدد العقود على النوع: النوع المستخدم لا يُحذف ولا تتغير طريقة فوترته.</summary>
    public int ContractsCount { get; set; }
}

/// <summary>إنشاء/تعديل نوع عقد؛ الرمز يُحدَّد عند الإنشاء ولا يتغير بعده (العقود تحفظه).</summary>
public class SaveContractTypeDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Workflow { get; set; } = "full";
    public string Billing { get; set; } = "after_receipt";
    public decimal? SimpleLimit { get; set; }
    public bool RequiresGuarantee { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>كتالوج دورة العقود العام (كل المنشآت): الحالات والانتقالات، والشروط والإجراءات المتاحة لها.</summary>
public class ContractWorkflowCatalogDto
{
    public List<ContractStageAdminDto> Stages { get; set; } = new();
    public List<ContractTransitionAdminDto> Transitions { get; set; } = new();
    /// <summary>الشروط التي تعرفها دالة الانتقال (غيرها يُغلق الانتقال).</summary>
    public List<string> Conditions { get; set; } = new();
    /// <summary>إجراءات شاشة العقود المتاحة للحالات والانتقالات (الرمز واسمه).</summary>
    public List<ContractActionOptionDto> Actions { get; set; } = new();
}

public class ContractActionOptionDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}

public class ContractStageAdminDto
{
    public string Id { get; set; } = string.Empty;
    public int Number { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string ShortNameAr { get; set; } = string.Empty;
    public string ShortNameEn { get; set; } = string.Empty;
    public string DescAr { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public int? SlaDays { get; set; }
    /// <summary>إجراءات المستندات المسموحة في الحالة (تعديل، ترحيل، استلام...).</summary>
    public List<string> AllowedActions { get; set; } = new();
}

/// <summary>تعديل حالة: الأسماء والوصف والمدة المستهدفة والإجراءات المسموحة (المعرّف والترتيب والمرحلة ثابتة؛ يعتمد عليها الكود).</summary>
public class SaveContractStageDto
{
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string ShortNameAr { get; set; } = string.Empty;
    public string ShortNameEn { get; set; } = string.Empty;
    public string DescAr { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;
    public int? SlaDays { get; set; }
    public List<string> AllowedActions { get; set; } = new();
}

public class ContractTransitionAdminDto
{
    public int Id { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public List<string> RequiredActions { get; set; } = new();
    public string? Condition { get; set; }
    /// <summary>full | simple | فارغ = كل المسارات.</summary>
    public string? Workflow { get; set; }
    public bool RequiresNote { get; set; }
    public string Tone { get; set; } = "primary";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>إضافة/تعديل انتقال؛ الحالتان تُحدَّدان عند الإضافة فقط.</summary>
public class SaveContractTransitionDto
{
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public List<string> RequiredActions { get; set; } = new();
    public string? Condition { get; set; }
    public string? Workflow { get; set; }
    public bool RequiresNote { get; set; }
    public string Tone { get; set; } = "primary";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
