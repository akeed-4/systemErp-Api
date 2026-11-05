namespace ERP.Core.Models.Accounting;

/// <summary>حالات المستخلص: مسودة ← مقدَّم ← معتمد ← مفوتر، أو ملغى (من المسودة أو المقدَّم).</summary>
public static class ContractCertificateStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Invoiced = "invoiced";
    public const string Cancelled = "cancelled";

    /// <summary>مستخلص مفتوح: لم يُعتمد ولم يُلغَ (واحد فقط لكل عقد).</summary>
    public static bool IsOpen(string status) => status is Draft or Submitted;
}

/// <summary>جاري (interim) أو ختامي (final): الختامي آخر مستخلص ويُشترط للاستلام الأولي.</summary>
public static class ContractCertificateKinds
{
    public const string Interim = "interim";
    public const string Final = "final";

    public static bool IsValid(string? kind) => kind is Interim or Final;
}

/// <summary>
/// مستخلص أعمال على عقد بجدول كميات (فوترة boq_certificate): يقيس لكل بند الكمية المنفذة السابقة والحالية والتراكمية.
/// السابق يحسبه الخادم من المستخلصات المعتمدة/المفوترة وحدها، والحالي يدخله المستخدم، والقيم كلها من الخادم.
/// </summary>
public class ContractCertificate : BaseEntity
{
    public Guid ContractId { get; set; }
    /// <summary>رقم المستخلص داخل العقد (1، 2، 3...).</summary>
    public int SequenceNo { get; set; }
    /// <summary>رقم المستند من عدّاد contract_certificate.</summary>
    public string CertificateNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = ContractCertificateKinds.Interim;
    public string Status { get; set; } = ContractCertificateStatuses.Draft;
    public DateTime CertificateDate { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }

    /// <summary>قيمة الأعمال (قبل الضريبة والخصومات): السابقة، الحالية = التراكمية − السابقة، التراكمية.</summary>
    public decimal PreviousWorkValue { get; set; }
    public decimal CurrentWorkValue { get; set; }
    public decimal CumulativeWorkValue { get; set; }

    // ---- الخصومات والضريبة على الأعمال الحالية (يحسبها الخادم؛ الغرامة والخصومات الأخرى يدخلها المُعِد) ----
    // الأساس الضريبي = الأعمال الحالية − استرداد الدفعة المقدمة (الدفعة ضُرِّبت في فاتورتها)؛ الضمان والغرامة والأخرى تُخصم بعد الضريبة.
    /// <summary>استرداد الدفعة المقدمة = الحالي × نسبة الدفعة، بحد المتبقي منها؛ الختامي يسترد الباقي كله.</summary>
    public decimal AdvanceRecovery { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }
    /// <summary>ضمان حسن التنفيذ المحتجز = الحالي × نسبة الضمان في العقد.</summary>
    public decimal RetentionAmount { get; set; }
    /// <summary>غرامة التأخير (يدخلها المُعِد) بحد أقصى تراكمي = قيمة العقد × أقصى نسبة غرامة.</summary>
    public decimal PenaltyAmount { get; set; }
    /// <summary>مجموع الخصومات الأخرى (<see cref="Deductions"/>).</summary>
    public decimal OtherDeductions { get; set; }
    /// <summary>الصافي المستحق = الإجمالي بالضريبة − الضمان − الغرامة − الخصومات الأخرى (لا يكون سالباً).</summary>
    public decimal NetPayable { get; set; }

    public string? Notes { get; set; }
    /// <summary>سبب آخر إعادة للمسودة أو الإلغاء.</summary>
    public string? ReturnNote { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>فاتورة المستخلص: بالخاضع والضريبة، واستقطاعاته (ضمان/غرامة/أخرى) أجزاء غير آجلة على حساباتها.</summary>
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoicedAt { get; set; }
    /// <summary>قيد استرداد الدفعة المقدمة المرافق للفاتورة (إن وُجد استرداد).</summary>
    public Guid? AdvanceJournalEntryId { get; set; }

    public virtual ICollection<ContractCertificateLine> Lines { get; set; } = new List<ContractCertificateLine>();
    public virtual ICollection<ContractCertificateDeduction> Deductions { get; set; } = new List<ContractCertificateDeduction>();
}

/// <summary>خصم آخر على المستخلص (مواد مورّدة من المالك، استهلاك خدمات، تأمينات...) يُخصم من الصافي بعد الضريبة.</summary>
public class ContractCertificateDeduction : BaseEntity
{
    public Guid CertificateId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }

    public virtual ContractCertificate Certificate { get; set; } = null!;
}

/// <summary>سطر بند في المستخلص: نسخة من بيانات البند وقت الإنشاء، والكميات والقيم التي يحسبها الخادم.</summary>
public class ContractCertificateLine : BaseEntity
{
    public Guid CertificateId { get; set; }
    public Guid ContractItemId { get; set; }

    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string LineKind { get; set; } = ContractBoqLineKinds.Measured;
    public decimal UnitPrice { get; set; }
    /// <summary>الكمية المعتمدة للبند وقت المستخلص، ونسبة السماح بتجاوزها.</summary>
    public decimal ContractQuantity { get; set; }
    public decimal OverrunPercent { get; set; }
    public int SortOrder { get; set; }

    public decimal PreviousQuantity { get; set; }
    public decimal CurrentQuantity { get; set; }
    public decimal CumulativeQuantity { get; set; }
    public decimal PreviousAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    public decimal CumulativeAmount { get; set; }
    /// <summary>نسبة الإنجاز التراكمية = التراكمية ÷ الكمية المعتمدة × 100.</summary>
    public decimal CompletionPercent { get; set; }

    public virtual ContractCertificate Certificate { get; set; } = null!;
}
