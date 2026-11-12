namespace ERP.Core.Models.Accounting;

/// <summary>
/// نوع عقد تديره المنشأة: مساره (كامل/مبسط) وطريقة فوترته (بعد الاستلام/مباشرة). العقد الأقل من <see cref="SimpleLimit"/>
/// يمشي مبسطاً تلقائياً ولو كان النوع كاملاً. <see cref="Code"/> هو ما يُحفظ في CommercialContract.ContractType.
/// </summary>
public class ContractTypeDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>full | simple</summary>
    public string Workflow { get; set; } = ContractWorkflows.Full;
    /// <summary>after_receipt | direct</summary>
    public string Billing { get; set; } = ContractBilling.AfterReceipt;
    /// <summary>العقد الذي قيمته أقل من هذا المبلغ يمشي مبسطاً؛ null = لا تبسيط بالقيمة.</summary>
    public decimal? SimpleLimit { get; set; }
    /// <summary>يلزم ضمان بنكي قبل التفعيل (للمسار الكامل).</summary>
    public bool RequiresGuarantee { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public static class ContractWorkflows
{
    public const string Full = "full";
    public const string Simple = "simple";

    public static bool IsValid(string? workflow) => workflow is Full or Simple;
}

public static class ContractBilling
{
    public const string AfterReceipt = "after_receipt";
    public const string Direct = "direct";
    /// <summary>مقاولات بجدول كميات: الاستحقاق من مستخلصات تقيس الكمية المنفذة لكل بند (سابق/حالي/تراكمي)، لا دفعات ولا بيانات تسليم.</summary>
    public const string BoqCertificate = "boq_certificate";

    public static bool IsValid(string? billing) => billing is AfterReceipt or Direct or BoqCertificate;
}

/// <summary>الشروط المسمّاة التي تعرفها دالة الانتقال (ContractWorkflowService.BlockersAsync).</summary>
public static class ContractTransitionConditions
{
    public const string CompleteData = "complete_data";
    public const string BothReviews = "both_reviews";
    public const string SignedGuaranteeLimit = "signed_guarantee_limit";
    public const string AllReceived = "all_received";
    public const string AllBilledRetentionReleased = "all_billed_retention_released";

    public static readonly string[] All = { CompleteData, BothReviews, SignedGuaranteeLimit, AllReceived, AllBilledRetentionReleased };
}

/// <summary>نوع بند جدول الكميات.</summary>
public static class ContractBoqLineKinds
{
    /// <summary>بند بكمية تُقاس.</summary>
    public const string Measured = "measured";
    /// <summary>مبلغ مقطوع: كميته 1 ويُقاس بنسبة إنجاز (كمية كسرية).</summary>
    public const string LumpSum = "lump_sum";
    /// <summary>مبلغ احتياطي يُصرف بقدر المنفذ.</summary>
    public const string Provisional = "provisional";

    public static bool IsValid(string? kind) => kind is Measured or LumpSum or Provisional;
}

/// <summary>
/// انتقال مسموح بين حالتين: من يقوم به (أي إجراء من <see cref="RequiredActions"/> على شاشة العقود)، هل يلزمه سبب،
/// والشرط المسمّى الذي تتحقق منه دالة الانتقال. يُزرع مع الترحيل ويُعدَّل من قاعدة البيانات لا من الكود.
/// </summary>
public sealed class ContractTransition
{
    public int Id { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    /// <summary>اسم الزر.</summary>
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>رموز إجراءات الصلاحيات مفصولة بفواصل؛ يكفي أحدها.</summary>
    public string RequiredActions { get; set; } = string.Empty;
    /// <summary>مفتاح الشرط في دالة الانتقال (complete_data, both_reviews, signed_guarantee_limit, all_received, all_billed_retention_released)؛ فارغ = بلا شرط.</summary>
    public string? Condition { get; set; }
    /// <summary>المسار الذي يُتاح له الانتقال: full | simple؛ فارغ = كل المسارات.</summary>
    public string? Workflow { get; set; }
    public bool RequiresNote { get; set; }
    /// <summary>primary | danger | neutral — لون الزر.</summary>
    public string Tone { get; set; } = "primary";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>قرار مراجع (قانوني أو مالي) على العقد في جولة مراجعة؛ الإعادة للمسودة تبدأ جولة جديدة.</summary>
public class ContractApproval : BaseEntity
{
    public Guid ContractId { get; set; }
    public int Round { get; set; }
    /// <summary>legal | financial</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>approved | returned</summary>
    public string Decision { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime DecidedAt { get; set; }

    // ---- مصدر القرار (فارغ = مراجعة يدوية من شاشة العقد) ----
    /// <summary><see cref="ContractApprovalSources"/>: من أين جاء القرار إن لم يكن مراجعة يدوية.</summary>
    public string? Source { get; set; }
    /// <summary>مرجع المصدر المقروء (رقم دراسة الجدوى FS-...).</summary>
    public string? SourceReference { get; set; }
    /// <summary>سجل المصدر (نسخة الدراسة المجمّدة <see cref="ProjectSnapshot"/> عند اعتماد الخطة).</summary>
    public Guid? SourceId { get; set; }
    /// <summary>قيمة العقد التي غطّاها القرار الآلي؛ أي اختلاف عنها يُسقطه.</summary>
    public decimal? ApprovedValue { get; set; }
    /// <summary>متى أُسقط القرار الآلي (الرجوع للمسودة أو تغيّر القيمة)؛ يبقى السجل بمعتمِده وتاريخه للتدقيق.</summary>
    public DateTime? InvalidatedAt { get; set; }
    /// <summary><see cref="ContractApprovalSources.DraftReset"/> | <see cref="ContractApprovalSources.ValueChanged"/></summary>
    public string? InvalidationReason { get; set; }
}

/// <summary>مصادر قرار المراجعة غير اليدوية وأسباب إسقاطها.</summary>
public static class ContractApprovalSources
{
    /// <summary>الموافقة المالية الآلية من دراسة جدوى معتمدة عند «إنشاء العقد» (الجولة الأولى فقط).</summary>
    public const string FeasibilityStudy = "feasibility_study";
    public const string DraftReset = "draft_reset";
    public const string ValueChanged = "value_changed";
}

/// <summary>سطر في سجل انتقالات العقد: من أي حالة لأي حالة، من قام ومتى، وملاحظته.</summary>
public class ContractStatusLog : BaseEntity
{
    public Guid ContractId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public int? TransitionId { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// ربط فاتورة (أو إشعار دائن) من جداول الفواتير المركزية بالعقد ونوعها ومبالغ حسابها؛ الفاتورة نفسها وقيدها وZATCA في محرك الفوترة.
/// </summary>
public class ContractInvoice : BaseEntity
{
    public Guid ContractId { get; set; }
    /// <summary>advance | progress | final | retention_release | credit_note</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>الفاتورة؛ فارغ للإفراج عن الضمان (قيد بلا فاتورة: الضريبة سبق احتسابها في فواتير المستخلصات).</summary>
    public Guid? InvoiceId { get; set; }
    /// <summary>رقم الفاتورة، أو رقم القيد للإفراج عن الضمان.</summary>
    public string InvoiceNumber { get; set; } = string.Empty;
    /// <summary>القيد المرافق: استرداد الدفعة المقدمة، إعادة تصنيف دفعة مقاول الباطن، أو قيد الإفراج عن الضمان.</summary>
    public Guid? JournalEntryId { get; set; }
    /// <summary>المستخلص المفوتر (فوترة جدول الكميات).</summary>
    public Guid? CertificateId { get; set; }
    /// <summary>بيان الاستلام المفوتر (فوترة بعد الاستلام).</summary>
    public Guid? DeliveryNoteId { get; set; }
    /// <summary>للإشعار الدائن: المرتجع الذي سبّبه والفاتورة الأصلية.</summary>
    public Guid? ReturnNoteId { get; set; }
    public Guid? RelatedInvoiceId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal AdvanceDeduction { get; set; }
    public decimal RetentionAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime IssuedAt { get; set; }
}

/// <summary>مرفق عقد (النسخة الموقعة، الضمان البنكي...) محفوظ في قاعدة البيانات.</summary>
public class ContractAttachment : BaseEntity
{
    public Guid ContractId { get; set; }
    /// <summary>signed_copy | guarantee | other</summary>
    public string Kind { get; set; } = "other";
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string? UploadedBy { get; set; }
}
