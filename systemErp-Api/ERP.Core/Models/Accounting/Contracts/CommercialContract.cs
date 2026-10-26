
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
    public decimal VatRate { get; set; } = 15m;
    public decimal VatAmount { get; set; }
    public decimal TotalValueWithVat { get; set; }

    public decimal TotalInvoiced { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal RemainingBalance { get; set; }

    public string? RevenueAccountCode { get; set; }
    public string? ReceivableAccountCode { get; set; }
    public string? Notes { get; set; }
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

    public virtual ICollection<ContractMilestone> Milestones { get; set; } = new List<ContractMilestone>();
    public virtual ICollection<ContractClause> Clauses { get; set; } = new List<ContractClause>();
    public virtual ICollection<CommercialContractItem> Items { get; set; } = new List<CommercialContractItem>();

}
