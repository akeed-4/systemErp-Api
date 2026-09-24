namespace ERP.Core.Models.POS;

/// <summary>سلة معلّقة يستأنفها الكاشير لاحقاً (لا أثر مالي ولا مخزني حتى إتمام الدفع).</summary>
public class PosHeldCart : BaseEntity
{
    public string CartReference { get; set; } = string.Empty; // HOLD-000001
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? AppliedCouponCode { get; set; }
    public int? LoyaltyPointsUsed { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<PosHeldCartItem> Items { get; set; } = new List<PosHeldCartItem>();
}
