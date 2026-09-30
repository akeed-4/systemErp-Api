namespace ERP.Core.DTOs.Shared;

public class InventoryCountApprovalDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string ApprovalNumber { get; set; } = string.Empty;
    public Guid InventoryCountId { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public InventoryCountScope Scope { get; set; }
    public InventoryCountApprovalStatus Status { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionComment { get; set; }
    public int VarianceLines { get; set; }
    public decimal TotalSurplusValue { get; set; }
    public decimal TotalShortageValue { get; set; }
    public decimal NetVarianceValue { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public int CurrentLevel { get; set; }
    public int TotalLevels { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? JournalEntryNumber { get; set; }
}
