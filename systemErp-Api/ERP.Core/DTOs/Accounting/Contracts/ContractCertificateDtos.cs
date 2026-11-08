namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>مستخلص أعمال بأسطره (للقراءة؛ كل الكميات السابقة والقيم من الخادم).</summary>
public class ContractCertificateDto
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public int SequenceNo { get; set; }
    public string CertificateNumber { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CertificateDate { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public decimal PreviousWorkValue { get; set; }
    public decimal CurrentWorkValue { get; set; }
    public decimal CumulativeWorkValue { get; set; }
    public decimal AdvanceRecovery { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal RetentionAmount { get; set; }
    public decimal PenaltyAmount { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPayable { get; set; }
    public List<ContractCertificateDeductionDto> Deductions { get; set; } = new();
    public string? Notes { get; set; }
    public string? ReturnNote { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoicedAt { get; set; }
    /// <summary>للمفوتر: المحصّل على فاتورته والمتبقي منها (الصافي المستحق ناقص التحصيل)؛ يُحسبان عند القراءة.</summary>
    public decimal Collected { get; set; }
    public decimal AmountDue { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<ContractCertificateLineDto> Lines { get; set; } = new();
    /// <summary>التكلفة الفعلية لكميات المواد المختارة، وقيد اعترافها عند الفوترة (تكلفة إيرادات / مواد تحت التنفيذ).</summary>
    public decimal MaterialCost { get; set; }
    public Guid? CostJournalEntryId { get; set; }
    public Guid? AdvanceJournalEntryId { get; set; }
    /// <summary>إلغاء الفاتورة: إشعارها الدائن/المدين ووقته ومنفّذه.</summary>
    public Guid? CreditNoteInvoiceId { get; set; }
    public string? CreditNoteNumber { get; set; }
    public DateTime? InvoiceCancelledAt { get; set; }
    public string? InvoiceCancelledBy { get; set; }
    /// <summary>كميات أذونات تسليم المواد المختارة في المستخلص (مرجع الإذن وتكلفته الفعلية).</summary>
    public List<ContractCertificateMaterialDto> Materials { get; set; } = new();
}

public class ContractCertificateMaterialDto
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public Guid IssueLineId { get; set; }
    public Guid ContractItemId { get; set; }
    public Guid ProductId { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>
/// سطر إذن تسليم مرحّل مؤهَّل للاختيار في مستخلص، على بنده في جدول الكميات: المسلَّم وتكلفته الفعلية، وما اختير منه في مستخلصات
/// أخرى غير ملغاة، والمتبقي، والمختار في المستخلص الحالي.
/// </summary>
public class CertificateMaterialOptionDto
{
    public Guid IssueId { get; set; }
    public Guid IssueLineId { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string BoqDescription { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal DeliveredQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>المختار في مستخلصات أخرى غير ملغاة (المفوترة وغير المفوترة).</summary>
    public decimal BilledQuantity { get; set; }
    public decimal BilledCost { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal RemainingCost { get; set; }
    /// <summary>المختار في المستخلص المطلوب (عند تعديله).</summary>
    public decimal SelectedQuantity { get; set; }
    /// <summary>بلا أصل تحت التنفيذ (رُحِّل بجرد دوري): يُختار للربط ولا يولّد قيد تكلفة عند الفوترة.</summary>
    public bool CostRecognizedAtDelivery { get; set; }
}

public class ContractCertificateLineDto
{
    public Guid Id { get; set; }
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string LineKind { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
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
    /// <summary>نسبة إنجاز هذا المستخلص وحده = الكمية الحالية ÷ الكمية المعتمدة × 100 (يحسبها الخادم من لقطة السطر).</summary>
    public decimal CurrentCompletionPercent { get; set; }
}

public class ContractCertificateDeductionDto
{
    public Guid Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// إنشاء/تعديل مسودة مستخلص: المستخدم يرسل الكمية الحالية لكل بند (غير المرسل صفر)، وغرامة التأخير والخصومات الأخرى.
/// استرداد الدفعة والضمان والضريبة والصافي يحسبها الخادم.
/// </summary>
public class SaveContractCertificateDto
{
    public string Kind { get; set; } = "interim";
    public DateTime? CertificateDate { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public string? Notes { get; set; }
    public decimal PenaltyAmount { get; set; }
    public List<SaveContractCertificateLineDto> Lines { get; set; } = new();
    public List<SaveContractCertificateDeductionDto> Deductions { get; set; } = new();
    /// <summary>
    /// كميات أذونات التسليم المختارة (تحل محل السابقة). فارغ (null) عند التعديل = تبقى المختارة كما هي، فلا يُسقطها عميل قديم.
    /// </summary>
    public List<SaveContractCertificateMaterialDto>? Materials { get; set; }
}

public class SaveContractCertificateMaterialDto
{
    public Guid IssueLineId { get; set; }
    public decimal Quantity { get; set; }
}

public class SaveContractCertificateDeductionDto
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class SaveContractCertificateLineDto
{
    public Guid ContractItemId { get; set; }
    public decimal CurrentQuantity { get; set; }
}

/// <summary>
/// تحصيل (عقد عميل) أو سداد (عقد مورد) فاتورة من فواتير العقد — مستخلص أو دفعة مقدمة — بسند يُوزَّع عليها.
/// المبلغ لا يتجاوز المتبقي على الفاتورة؛ طريقة الدفع المؤسَّسة تحدد حساب الخزينة.
/// </summary>
public class ContractPaymentRequestDto
{
    /// <summary>فاتورة العقد (من فواتير المستخلصات أو الدفعة المقدمة لهذا العقد).</summary>
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime? Date { get; set; }
    /// <summary>طريقة الدفع المؤسَّسة (نقد/بنك/شيك...)؛ وإلا حساب الخزينة صراحةً.</summary>
    public string? PaymentMethodCode { get; set; }
    public string? TreasuryAccountCode { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

/// <summary>قيد الإفراج عن الضمان المحتجز: يعيد المبلغ لذمة الطرف ليُحصَّل/يُسدَّد.</summary>
public class RetentionReleaseResultDto
{
    public Guid JournalEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>إجراء على المستخلص؛ الملاحظة إلزامية للإعادة والإلغاء.</summary>
public class ContractCertificateActionDto
{
    public string? Note { get; set; }
}

/// <summary>موقف جدول الكميات: لكل بند المعتمد والمنفذ (من المستخلصات المعتمدة/المفوترة) والمتبقي.</summary>
public class BoqProgressDto
{
    public Guid ContractId { get; set; }
    public decimal ContractValue { get; set; }
    public decimal CertifiedValue { get; set; }
    public decimal RemainingValue { get; set; }
    public decimal CompletionPercent { get; set; }
    public int ApprovedCertificates { get; set; }
    public bool HasFinalCertificate { get; set; }
    /// <summary>المستخلص المفتوح (مسودة/مقدَّم) إن وُجد: لا يُنشأ غيره حتى يُعتمد أو يُلغى.</summary>
    public Guid? OpenCertificateId { get; set; }

    // ---- الخصومات التراكمية من المستخلصات المعتمدة/المفوترة (وشروط العقد التي تُحسب بها) ----
    public decimal AdvancePercent { get; set; }
    public decimal RetentionPercent { get; set; }
    public decimal VatRate { get; set; }
    public decimal MaxPenaltyPercent { get; set; }
    /// <summary>الدفعة المقدمة التعاقدية = قيمة العقد × نسبة الدفعة.</summary>
    public decimal AdvanceAmount { get; set; }
    public decimal AdvanceRecovered { get; set; }
    public decimal AdvanceOutstanding { get; set; }
    public decimal RetentionHeld { get; set; }
    public decimal PenaltiesTotal { get; set; }
    /// <summary>سقف الغرامات = قيمة العقد × أقصى نسبة غرامة (0 = بلا سقف).</summary>
    public decimal PenaltyCap { get; set; }
    public decimal OtherDeductionsTotal { get; set; }
    public decimal NetPayableTotal { get; set; }

    // ---- الفوترة والتحصيل ----
    /// <summary>صافي فاتورة الدفعة المقدمة (قبل الضريبة)؛ الاسترداد يقتصر عليه.</summary>
    public decimal AdvanceBilled { get; set; }
    public string? AdvanceInvoiceNumber { get; set; }
    /// <summary>فاتورة الدفعة المقدمة والمتبقي عليها (لتحصيلها/سدادها من شاشة العقد).</summary>
    public Guid? AdvanceInvoiceId { get; set; }
    public decimal AdvanceAmountDue { get; set; }
    /// <summary>الضمان المحتجز في الفواتير، والمُفرج عنه، والمتبقي للإفراج.</summary>
    public decimal RetentionInvoiced { get; set; }
    public decimal RetentionReleased { get; set; }
    public decimal RetentionOutstanding { get; set; }
    /// <summary>إجمالي فواتير العقد (الدفعة والمستخلصات شاملة الضريبة)، والمحصّل عليها من السندات الموزّعة.</summary>
    public decimal InvoicedTotal { get; set; }
    public decimal CollectedTotal { get; set; }
    /// <summary>جدولة المستخلصات المتفق عليها في العقد ومكان كل مستخلص منها.</summary>
    public CertificateScheduleDto Schedule { get; set; } = new();
    public List<BoqProgressLineDto> Lines { get; set; } = new();
}

/// <summary>
/// جدول مستخلصات العقد كما يحسبه الخادم: الدفعات/الفترات المتفق عليها، والمستخلص الذي غطّى كلاً منها، وما يليها.
/// المستخلصات غير الملغاة تُسند للخانات بترتيبها.
/// </summary>
public class CertificateScheduleDto
{
    /// <summary>on_demand | installments | periodic</summary>
    public string Mode { get; set; } = "on_demand";
    public int? Count { get; set; }
    public int? PeriodMonths { get; set; }
    /// <summary>المستخلصات غير الملغاة حتى الآن.</summary>
    public int Used { get; set; }
    /// <summary>لا مستخلص جديد: اكتملت الدفعات أو اعتُمد الختامي.</summary>
    public bool Complete { get; set; }
    /// <summary>المستخلص التالي: رقمه في الجدول، وفترته (الدوري)، وهل يجب أن يكون ختامياً (آخر دفعة).</summary>
    public int? NextNumber { get; set; }
    public DateTime? NextPeriodFrom { get; set; }
    public DateTime? NextPeriodTo { get; set; }
    /// <summary>أقرب تاريخ لتقديم المستخلص التالي (نهاية فترته، أو موعد الدفعة المخطط).</summary>
    public DateTime? NextDueDate { get; set; }
    public bool NextMustBeFinal { get; set; }
    public List<CertificateScheduleSlotDto> Slots { get; set; } = new();
}

public class CertificateScheduleSlotDto
{
    public int Number { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    /// <summary>موعد الاستحقاق: نهاية الفترة (الدوري)، أو موعد موزّع بالتساوي على مدة العقد (الدفعات).</summary>
    public DateTime? DueDate { get; set; }
    public Guid? CertificateId { get; set; }
    public string? CertificateNumber { get; set; }
    public string? CertificateStatus { get; set; }
    public string? Kind { get; set; }
    public decimal? CurrentWorkValue { get; set; }
    public decimal? NetPayable { get; set; }
    /// <summary>done (معتمد/مفوتر) | open (مسودة/مقدَّم) | due (حلّ موعده) | upcoming | closed (بعد الختامي)</summary>
    public string State { get; set; } = "upcoming";
}

public class BoqProgressLineDto
{
    public Guid ContractItemId { get; set; }
    public string? LineCode { get; set; }
    public string? SectionName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string LineKind { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal ContractQuantity { get; set; }
    public decimal OverrunPercent { get; set; }
    /// <summary>أقصى كمية تراكمية مسموحة = المعتمدة × (1 + السماح%).</summary>
    public decimal MaxQuantity { get; set; }
    public decimal CertifiedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal CertifiedAmount { get; set; }
    public decimal CompletionPercent { get; set; }
}
