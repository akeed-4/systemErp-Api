namespace ERP.Core.DTOs.POS;

public partial class CreateCustomerLoyaltyDto
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int PointsBalance { get; set; }
    public int TotalPointsEarned { get; set; }
    public int TotalPointsRedeemed { get; set; }
    public decimal PointsValueSar { get; set; }
    public LoyaltyTier Tier { get; set; } = LoyaltyTier.Bronze;
}
