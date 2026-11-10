namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>
/// تقرير تكلفة المشروع وربحيته من القيود المرحّلة على مركز تكلفته (مصدر الحقيقة)، مقابل المخطط، مع الإنجاز والتوقع عند الاكتمال.
/// كل القيم قبل الضريبة.
/// </summary>
public class ProjectCostReportDto
{
    public Guid ContractId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string? CostCenterCode { get; set; }
    /// <summary>العقد بلا مركز تكلفة: لا قيود تُنسب له (التقرير فارغ).</summary>
    public bool MissingCostCenter { get; set; }

    // ---- القيمة والمخطط ----
    public decimal ContractValue { get; set; }
    /// <summary>التكلفة التقديرية من جدول الكميات (دراسة الجدوى) بعد أوامر التغيير.</summary>
    public decimal EstimatedCost { get; set; }
    /// <summary>الميزانية المدخلة في ملف المشروع.</summary>
    public decimal ProjectBudget { get; set; }
    /// <summary>الميزانية عند الاكتمال المعتمدة للحساب (التكلفة التقديرية، وإلا الميزانية).</summary>
    public decimal BudgetAtCompletion { get; set; }
    public decimal PlannedMargin { get; set; }

    // ---- الإنجاز ----
    /// <summary>الأعمال المعتمدة (المستخلصات المعتمدة والمفوترة) ونسبتها من قيمة العقد.</summary>
    public decimal CertifiedValue { get; set; }
    public decimal ProgressPercent { get; set; }

    // ---- الفعلي من القيود ----
    public decimal ActualCost { get; set; }
    /// <summary>منها ما زال أصلاً (مواد تحت التنفيذ لم تُعترف تكلفتها بعد).</summary>
    public decimal WipBalance { get; set; }
    /// <summary>منها ما اعتُرف مصروفاً/تكلفة إيرادات في الدفاتر.</summary>
    public decimal ExpensedCost { get; set; }
    /// <summary>الإيراد المقيَّد على مركز المشروع (أساس الفوترة).</summary>
    public decimal BookedRevenue { get; set; }
    /// <summary>غرامات وخصومات العميل على مستخلصاتنا (تخفض صافي الإيراد).</summary>
    public decimal ClientDeductions { get; set; }
    /// <summary>الربح الدفتري حتى الآن = الإيراد − الخصومات − التكلفة المعترف بها.</summary>
    public decimal BookedProfit { get; set; }

    // ---- المباشر وغير المباشر وصافي الربح ----
    /// <summary>التكاليف المباشرة = الفعلي − غير المباشرة (مواد، عمالة، معدات مملوكة ومستأجرة، باطن، أخرى مباشرة).</summary>
    public decimal DirectCost { get; set; }
    /// <summary>التكاليف غير المباشرة المحمّلة على المشروع بقيود التوزيع.</summary>
    public decimal IndirectCost { get; set; }
    /// <summary>صافي الإيراد = الإيراد المقيَّد − غرامات العميل وخصوماته.</summary>
    public decimal NetRevenue { get; set; }
    /// <summary>هامش الربح الدفتري من صافي الإيراد (فارغ بلا إيراد).</summary>
    public decimal? NetMarginPercent { get; set; }
    /// <summary>
    /// تكلفة مستبعدة لأنها محمّلة مرتين: رواتب موظفين وإهلاك أصول على مركز المشروع وهم يُحمَّلون عليه أيضاً بساعاتهم من التقارير اليومية.
    /// </summary>
    public decimal DuplicateExcluded { get; set; }

    // ---- القيمة المكتسبة والتوقع ----
    public decimal EarnedValue { get; set; }
    /// <summary>مؤشر أداء التكلفة = المكتسب ÷ الفعلي (فارغ بلا تكلفة أو بلا إنجاز).</summary>
    public decimal? CostPerformanceIndex { get; set; }
    /// <summary>التكلفة المتوقعة عند الاكتمال = الميزانية ÷ مؤشر الأداء (وبلا مؤشر: الفعلي + المتبقي من الميزانية).</summary>
    public decimal EstimateAtCompletion { get; set; }
    public decimal EstimateToComplete { get; set; }
    public decimal ForecastProfit { get; set; }
    public decimal? ForecastMarginPercent { get; set; }
    /// <summary>الفرق المتوقع عن الميزانية (موجب = تجاوز).</summary>
    public decimal ForecastOverrun { get; set; }

    // ---- نسبة الإنجاز (تكلفة إلى تكلفة) للعرض؛ القيود على أساس الفوترة ----
    public decimal PocPercent { get; set; }
    public decimal PocRevenue { get; set; }
    public decimal PocProfit { get; set; }

    /// <summary>التكلفة التشغيلية من التقارير اليومية المعتمدة (للمقارنة؛ لا تُرحَّل إلا بقيد التحميل).</summary>
    public decimal SiteOperatingCost { get; set; }
    /// <summary>تقارير يومية معتمدة لم يُرحَّل قيد تحميل عمالتها ومعداتها (اعتُمدت قبل التفعيل).</summary>
    public int UnpostedSiteReports { get; set; }

    public List<ProjectCostCategoryDto> Categories { get; set; } = new();
    public List<ProjectCostAccountDto> Accounts { get; set; } = new();
    /// <summary>كل أسطر التكلفة: القيد ومصدره وتاريخه وحسابه وفئته ومبلغه.</summary>
    public List<ProjectCostLineDto> Lines { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>فئة تكلفة: المخطط (حين يُعرف) والفعلي والفرق، وأكبر المستندات.</summary>
public class ProjectCostCategoryDto
{
    /// <summary>materials | labor | equipment | rented_equipment | subcontract | other | overhead | execution</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>direct | indirect</summary>
    public string Group { get; set; } = "direct";
    public decimal? Planned { get; set; }
    public decimal Actual { get; set; }
    public decimal? Variance { get; set; }
    public List<ProjectCostDocumentDto> TopDocuments { get; set; } = new();
}

public class ProjectCostDocumentDto
{
    public Guid JournalEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string? SourceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>سطر تكلفة: قيد واحد على حساب واحد في فئة واحدة (أو تسوية استبعاد مزدوج بلا قيد).</summary>
public class ProjectCostLineDto
{
    public Guid? JournalEntryId { get; set; }
    public string? EntryNumber { get; set; }
    public DateTime Date { get; set; }
    public string? SourceType { get; set; }
    public string? ReferenceNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Group { get; set; } = "direct";
    public decimal Amount { get; set; }
}

public class ProjectCostAccountDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
