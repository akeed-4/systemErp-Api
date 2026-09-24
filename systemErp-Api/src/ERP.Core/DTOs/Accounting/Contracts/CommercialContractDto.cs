namespace ERP.Core.DTOs.Accounting;

public partial class CommercialContractDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContractType { get; set; } = "supply";
    public string Stage { get; set; } = "draft";
    public ContractStatus Status { get; set; } = ContractStatus.Draft;
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
    public List<ContractMilestoneDto> Milestones { get; set; } = new();
    public List<ContractClauseDto> Clauses { get; set; } = new();
}
