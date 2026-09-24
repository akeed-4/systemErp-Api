
namespace ERP.Core.Models.Accounting;

public class ContractMilestone : BaseEntity
{
    public Guid ContractId { get; set; }
    public int MilestoneNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Percentage { get; set; }
    public decimal Amount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? CompletionDate { get; set; }
    public ContractMilestoneStatus Status { get; set; } = ContractMilestoneStatus.Pending;
    public DateTime? InvoicedAt { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Notes { get; set; }

    public virtual CommercialContract Contract { get; set; } = null!;
}
