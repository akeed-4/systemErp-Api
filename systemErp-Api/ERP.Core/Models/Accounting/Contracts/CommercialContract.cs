
namespace ERP.Core.Models.Accounting;

/// <summary>
/// العقود التجارية العامة - تمر بدورة معتمدة من 7 مراحل (انظر Stage)
/// تنتهي بفوترة المستخلصات (<see cref="ContractMilestone"/>) عبر محرك المحاسبة المركزي.
/// </summary>
public class CommercialContract : BaseEntity, IScopedRecord
{
    /// <summary>منشئ المستند (يُختم آلياً) لنطاق «السجلات التي أنشأها».</summary>
    public Guid? CreatedByUserId { get; set; }
    /// <summary>المستخدم المسند إليه المستند لنطاق «السجلات المسندة إليه».</summary>
    public Guid? AssignedToUserId { get; set; }
    /// <summary>موقع (معرض/مستودع) المستند: يُختم من موقع الجلسة عند الإنشاء؛ فارغ = مستند قديم يظهر لكل المواقع.</summary>
    public Guid? WarehouseId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    /// <summary>رمز نوع العقد (ContractTypeDefinition.Code): يحدّد المسار (كامل/مبسط) وطريقة الفوترة.</summary>
    public string ContractType { get; set; } = "supply_materials";
    /// <summary>حالة العقد (ContractStageDefinition): draft | review | approval | active | initial_acceptance | closed | suspended | cancelled. لا تتغير إلا عبر دالة الانتقال.</summary>
    public string Stage { get; set; } = "draft";
    /// <summary>ملخص قديم للحالة يُشتق من <see cref="Stage"/> (للتوافق مع الشاشات والتقارير القائمة).</summary>
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    /// <summary>متى دخل العقد حالته الحالية (لحساب التأخر عن المدة المستهدفة للحالة).</summary>
    public DateTime? StageChangedAt { get; set; }
    /// <summary>جولة المراجعة الحالية: تزيد مع كل إعادة للمسودة فتُلغى موافقات الجولة السابقة.</summary>
    public int ReviewRound { get; set; } = 1;
    /// <summary>عقد بمسار مبسط (من نوعه أو لأن قيمته دون حد التبسيط) يُثبَّت عند الإنشاء.</summary>
    public bool IsSimplified { get; set; }
    /// <summary>
    /// هذا العقد يلزمه ضمان بنكي (رقم ومبلغ وتاريخ انتهاء ساري) قبل تفعيله، على أي مسار. يختاره المستخدم لكل عقد؛
    /// العقود القائمة قبل الحقل أخذته من نوعها (يلزم ضمان + مسار كامل) كما كانت تُعامل.
    /// </summary>
    public bool RequiresGuarantee { get; set; }
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }

    public string PartyName { get; set; } = string.Empty;
    public string? PartyVatNumber { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public decimal ContractValue { get; set; }
    /// <summary>التكلفة التقديرية للمشروع (دراسة الجدوى): مجموع كميات الأسطر المسعّرة × تكلفتها التقديرية. يحسبها الخادم.</summary>
    public decimal EstimatedCost { get; set; }

    // ---- تسعير العطاء (عقود جدول الكميات) ----
    // سعر الوحدة المقترح = التكلفة المباشرة × (1 + مجموع النسب الثلاث). النسب تُحفظ مرجعاً لكيفية بناء العطاء،
    // والسعر النهائي يبقى سعر العطاء المُدخل في كل سطر.
    /// <summary>نسبة المصاريف غير المباشرة (إدارة الموقع، التأمينات، الضمانات) فوق التكلفة المباشرة.</summary>
    public decimal OverheadPercent { get; set; }
    /// <summary>نسبة احتياطي المخاطر فوق التكلفة المباشرة.</summary>
    public decimal RiskPercent { get; set; }
    /// <summary>نسبة الربح المستهدف فوق التكلفة المباشرة.</summary>
    public decimal ProfitPercent { get; set; }
    /// <summary>
    /// خصم التفاوض: يُطبَّق بالنسبة نفسها على سعر عطاء كل سطر، فيبقى مجموع جدول الكميات مساوياً لقيمة العقد. يحفظه الخادم صفراً لغير جدول الكميات.
    /// </summary>
    public decimal DiscountPercent { get; set; }
    /// <summary>قيمة العطاء قبل خصم التفاوض = مجموع الكمية × سعر العطاء. يحسبها الخادم (= قيمة العقد بلا خصم).</summary>
    public decimal TenderValue { get; set; }

    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalValueWithVat { get; set; }

    public decimal TotalInvoiced { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal RemainingBalance { get; set; }

    public string? RevenueAccountCode { get; set; }
    public string? ReceivableAccountCode { get; set; }
    public string? Notes { get; set; }
    /// <summary>بيانات المشروع (عقود المقاولات).</summary>
    public string? ProjectName { get; set; }
    public string? ProjectLocation { get; set; }
    public string? ConsultantName { get; set; }
    public string? TitleEn { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? PartyCrNumber { get; set; }
    public string? RepresentativeName { get; set; }
    public int? DurationMonths { get; set; }
    public bool AutoRenew { get; set; }
    public string Currency { get; set; } = "SAR";
    public Guid? CostCenterId { get; set; }
    /// <summary>نسبة الدفعة المقدمة من قيمة العقد؛ تُخصم بالنسبة نفسها من كل فاتورة مرحلية.</summary>
    public decimal AdvancePercent { get; set; }
    public decimal RetentionPercent { get; set; }
    public decimal RetentionAmount { get; set; }
    public DateTime? RetentionReleaseDate { get; set; }
    public decimal LatePenaltyPerDay { get; set; }
    public decimal MaxPenaltyPercent { get; set; }
    public string? ScopeOfWork { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? SignedByCompany { get; set; }
    public string? SignedByParty { get; set; }
    public DateTime? SignedDate { get; set; }
    /// <summary>الضمان البنكي (شرط التفعيل لأنواع العقود التي تتطلبه).</summary>
    public string? GuaranteeNumber { get; set; }
    public string? GuaranteeBank { get; set; }
    public decimal? GuaranteeAmount { get; set; }
    public DateTime? GuaranteeExpiry { get; set; }

    // ---- جدولة المستخلصات (عقود المقاولات بجدول كميات فقط؛ انظر ContractCertificateSchedules) ----
    /// <summary>on_demand (مستخلص متى طُلب) | installments (عدد دفعات محدد) | periodic (مستخلص كل فترة متفق عليها).</summary>
    public string CertificateSchedule { get; set; } = ContractCertificateSchedules.OnDemand;
    /// <summary>عدد دفعات المستخلصات (installments): آخرها ختامي، ولا يُنشأ مستخلص بعدها.</summary>
    public int? CertificateCount { get; set; }
    /// <summary>طول الفترة بالأشهر (periodic): 1 = شهري. تبدأ من بداية العقد وتنتهي بنهاية الشهر، ويُحسب المنجز في كل فترة.</summary>
    public int? CertificatePeriodMonths { get; set; }

    public virtual ICollection<ContractMilestone> Milestones { get; set; } = new List<ContractMilestone>();
    public virtual ICollection<ContractClause> Clauses { get; set; } = new List<ContractClause>();
    public virtual ICollection<CommercialContractItem> Items { get; set; } = new List<CommercialContractItem>();

}
