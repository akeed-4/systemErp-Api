
namespace ERP.Core.Models.Accounting;

/// <summary>
/// العقود التجارية العامة - تمر بدورة معتمدة من 7 مراحل (انظر Stage)
/// تنتهي بفوترة المستخلصات (<see cref="ContractMilestone"/>) عبر محرك المحاسبة المركزي.
/// </summary>
public class CommercialContract : BaseEntity
{
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContractType { get; set; } = "supply"; // supply | sla_service | maintenance | construction | lease | consulting | normal_purchasing | ...
    /// <summary>draft | legal_review | approved_signed | active_execution | milestone_billing | initial_inspection | final_closed، أو مراحل دورة الشراء العادية: normal_agreement | normal_delivery | normal_delivery_return | normal_milestone_invoice</summary>
    public string Stage { get; set; } = "draft";
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
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

    public virtual ICollection<ContractMilestone> Milestones { get; set; } = new List<ContractMilestone>();
    public virtual ICollection<ContractClause> Clauses { get; set; } = new List<ContractClause>();

}
