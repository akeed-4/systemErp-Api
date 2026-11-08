namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>مقاولو الباطن لمشروع (عقد عميل): عقودهم المرتبطة بموقفها، وتغطية بنود المشروع بأعمال الباطن.</summary>
public class ProjectSubcontractsDto
{
    public decimal ProjectValue { get; set; }
    /// <summary>قيمة عقود الباطن غير الملغاة (قبل الضريبة).</summary>
    public decimal SubcontractValue { get; set; }
    /// <summary>أعمال الباطن المعتمدة (المستخلصات المعتمدة والمفوترة) = تكلفة الباطن المستحقة.</summary>
    public decimal CertifiedValue { get; set; }
    public decimal InvoicedTotal { get; set; }
    public decimal RetentionHeld { get; set; }
    public List<ProjectSubcontractRowDto> Subcontracts { get; set; } = new();
    public List<SubcontractItemCoverageDto> Items { get; set; } = new();
}

public class ProjectSubcontractRowDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal ContractValue { get; set; }
    public decimal CertifiedValue { get; set; }
    public decimal CompletionPercent { get; set; }
    public decimal InvoicedTotal { get; set; }
    public decimal RetentionHeld { get; set; }
    public int OpenCertificates { get; set; }
    /// <summary>أسطر الباطن غير المرتبطة ببند في المشروع.</summary>
    public int UnlinkedLines { get; set; }
}

/// <summary>
/// بند المشروع وما أُسند منه للباطن: كمية المشروع وسعر بيعه والمعتمد للعميل، مقابل الكمية المسندة وسعرها والمعتمد للباطن.
/// المقارنة بالأرقام كما هي؛ الوحدة قد تختلف بين العقدين (تظهر الوحدتان).
/// </summary>
public class SubcontractItemCoverageDto
{
    public Guid MainItemId { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal MainQuantity { get; set; }
    public decimal MainUnitPrice { get; set; }
    public decimal MainCertifiedQuantity { get; set; }
    public decimal SubcontractedQuantity { get; set; }
    public decimal SubcontractValue { get; set; }
    /// <summary>متوسط سعر الباطن للوحدة (القيمة ÷ الكمية).</summary>
    public decimal SubcontractUnitPrice { get; set; }
    public decimal SubcontractCertifiedQuantity { get; set; }
    public string SubcontractUnits { get; set; } = string.Empty;
    /// <summary>هامش الوحدة = سعر البيع − متوسط سعر الباطن.</summary>
    public decimal UnitMargin { get; set; }
    /// <summary>المسند للباطن يتجاوز كمية المشروع.</summary>
    public bool OverSubcontracted { get; set; }
    public List<string> SubcontractNumbers { get; set; } = new();
}
