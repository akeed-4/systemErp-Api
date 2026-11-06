namespace ERP.Core.DTOs.Accounting;

public partial class CreateCommercialContractDto
{
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContractType { get; set; } = "supply_materials";
    /// <summary>
    /// مسار اعتماد هذا العقد: full | simple، يختاره المستخدم لكل عقد مستقلاً عن نوعه ويُحفظ في IsSimplified.
    /// null = عميل قديم أو طلب اعتماد معلّق قبل هذا الحقل: يُشتق من النوع وحد التبسيط كما كان. بالتعديل: null = يبقى كما هو.
    /// </summary>
    public string? Workflow { get; set; }
    /// <summary>
    /// يلزم هذا العقد ضمان بنكي قبل التفعيل؟ false = لا ضمان (تُمسح بياناته). null = عميل قديم: عند الإنشاء من النوع
    /// (يلزم ضمان + مسار كامل)، وبالتعديل يبقى كما هو.
    /// </summary>
    public bool? RequiresGuarantee { get; set; }
    // لا حالة هنا: العقد يبدأ «مسودة» دائماً ولا تتغير حالته إلا عبر move
    public Guid? PartyId { get; set; }
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? PartyVatNumber { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal ContractValue { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalValueWithVat { get; set; }
    public decimal TotalInvoiced { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal RemainingBalance { get; set; }
    public string? RevenueAccountCode { get; set; }
    public string? ReceivableAccountCode { get; set; }
    public string? Notes { get; set; }
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
    public string? GuaranteeNumber { get; set; }
    public string? GuaranteeBank { get; set; }
    public decimal? GuaranteeAmount { get; set; }
    public DateTime? GuaranteeExpiry { get; set; }
    /// <summary>جدولة المستخلصات (عقود المقاولات): on_demand | installments | periodic. null = حسب الطلب عند الإنشاء، ويبقى كما هو بالتعديل.</summary>
    public string? CertificateSchedule { get; set; }
    public int? CertificateCount { get; set; }
    public int? CertificatePeriodMonths { get; set; }
    public List<ContractMilestoneDto> Milestones { get; set; } = new();
    public List<ContractClauseDto> Clauses { get; set; } = new();
    public List<ContractItemDto> Items { get; set; } = new();
}
