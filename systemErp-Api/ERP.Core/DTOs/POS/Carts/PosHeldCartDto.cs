namespace ERP.Core.DTOs.POS;

public partial class PosHeldCartDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string CartReference { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? AppliedCouponCode { get; set; }
    public int? LoyaltyPointsUsed { get; set; }
    public string? Notes { get; set; }
    public List<PosHeldCartItemDto> Items { get; set; } = new();
}
