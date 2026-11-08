namespace ERP.Core.DTOs.Accounting;

public partial class CommercialContractDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContractType { get; set; } = "supply_materials";
    public string Stage { get; set; } = "draft";
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
    /// <summary>متى دخل العقد حالته الحالية، وجولة المراجعة، وهل يمشي مبسطاً (للقراءة فقط).</summary>
    public DateTime? StageChangedAt { get; set; }
    public int ReviewRound { get; set; }
    public bool IsSimplified { get; set; }
    /// <summary>يلزم هذا العقد ضمان بنكي قبل التفعيل.</summary>
    public bool RequiresGuarantee { get; set; }
    public Guid? PartyId { get; set; }
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? PartyVatNumber { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyEmail { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>موقع العقد (المعرض/المستودع) المختوم من موقع الجلسة عند الإنشاء.</summary>
    public Guid? WarehouseId { get; set; }
    public decimal ContractValue { get; set; }
    /// <summary>للقراءة: التكلفة التقديرية للمشروع من أسطر جدول الكميات.</summary>
    public decimal EstimatedCost { get; set; }
    /// <summary>تسعير العطاء: نسب المصاريف غير المباشرة والمخاطر والربح فوق التكلفة المباشرة.</summary>
    public decimal OverheadPercent { get; set; }
    public decimal RiskPercent { get; set; }
    public decimal ProfitPercent { get; set; }
    /// <summary>خصم التفاوض الموزّع على أسعار البنود.</summary>
    public decimal DiscountPercent { get; set; }
    /// <summary>للقراءة: قيمة العطاء قبل الخصم؛ الفرق عن <see cref="ContractValue"/> هو مبلغ الخصم.</summary>
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
    /// <summary>جدولة المستخلصات: on_demand | installments | periodic.</summary>
    public string CertificateSchedule { get; set; } = "on_demand";
    public int? CertificateCount { get; set; }
    public int? CertificatePeriodMonths { get; set; }
    public List<ContractMilestoneDto> Milestones { get; set; } = new();
    public List<ContractClauseDto> Clauses { get; set; } = new();
    public List<ContractItemDto> Items { get; set; } = new();
}
