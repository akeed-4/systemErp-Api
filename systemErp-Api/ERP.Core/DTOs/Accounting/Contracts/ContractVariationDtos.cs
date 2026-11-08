namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>أمر تغيير بأسطره (للقراءة؛ القيم والفروق من الخادم).</summary>
public class ContractVariationDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public int SequenceNo { get; set; }
    public string VariationNumber { get; set; } = string.Empty;
    public DateTime VariationDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ClientReference { get; set; }
    public int ExtensionDays { get; set; }
    public decimal ValueChange { get; set; }
    public decimal CostChange { get; set; }
    /// <summary>الضريبة على فرق القيمة بنسبة العقد.</summary>
    public decimal VatChange { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ReturnNote { get; set; }
    public string? Notes { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public decimal? ContractValueBefore { get; set; }
    public decimal? ContractValueAfter { get; set; }
    public DateTime? EndDateBefore { get; set; }
    public DateTime? EndDateAfter { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ContractVariationLineDto> Lines { get; set; } = new();
}

public class ContractVariationLineDto
{
    public Guid Id { get; set; }
    /// <summary>البند القائم (فارغ = بند جديد).</summary>
    public Guid? ContractItemId { get; set; }
    /// <summary>البند الذي أنشأه الاعتماد للسطر الجديد.</summary>
    public Guid? CreatedItemId { get; set; }
    public bool IsNew { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string LineKind { get; set; } = "measured";
    public decimal QuantityChange { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public decimal Amount { get; set; }
    public decimal CostAmount { get; set; }
    /// <summary>الكمية المعدلة للبند قبل الأمر وبعده: الحالية للمفتوح (معاينة)، والمثبّتة عند الاعتماد.</summary>
    public decimal? QuantityBefore { get; set; }
    public decimal? QuantityAfter { get; set; }
    /// <summary>المقاس للبند في المستخلصات غير الملغاة (حد أدنى للكمية بعد النقص).</summary>
    public decimal CertifiedQuantity { get; set; }
    public int SortOrder { get; set; }
    public string? Notes { get; set; }
}

/// <summary>إنشاء/تعديل مسودة أمر تغيير. الأسطر تحل محل السابقة.</summary>
public class SaveContractVariationDto
{
    public DateTime? VariationDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? ClientReference { get; set; }
    public int ExtensionDays { get; set; }
    public string? Notes { get; set; }
    public List<SaveContractVariationLineDto> Lines { get; set; } = new();
}

/// <summary>
/// سطر الأمر: لبند قائم يكفي <see cref="ContractItemId"/> وفرق الكمية (السعر والتكلفة والوصف من البند)؛
/// للبند الجديد الوصف والوحدة والسعر والكمية الموجبة، والتكلفة التقديرية اختيارية.
/// </summary>
public class SaveContractVariationLineDto
{
    public Guid? ContractItemId { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string? LineKind { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal EstimatedUnitCost { get; set; }
    public string? Notes { get; set; }
}

public class ContractVariationActionDto
{
    public string? Note { get; set; }
}

/// <summary>موقف قيمة العقد: الأصلية، والمعتمد من أوامر التغيير، والمعلق، والحالية، والمدة الأصلية والمعدلة.</summary>
public class ContractVariationSummaryDto
{
    public decimal OriginalValue { get; set; }
    public decimal ApprovedChange { get; set; }
    public decimal PendingChange { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal OriginalEstimatedCost { get; set; }
    public decimal CurrentEstimatedCost { get; set; }
    public DateTime? OriginalEndDate { get; set; }
    public DateTime? CurrentEndDate { get; set; }
    public int ApprovedExtensionDays { get; set; }
    public int ApprovedCount { get; set; }
    public int OpenCount { get; set; }
    public int RejectedCount { get; set; }
    public List<ContractVariationDto> Variations { get; set; } = new();
}
