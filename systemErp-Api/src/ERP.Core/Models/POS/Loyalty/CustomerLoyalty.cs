namespace ERP.Core.Models.POS;

/// <summary>رصيد ولاء عميل: نقطة لكل 10 ريال مشتريات، وقيمة النقطة الواحدة 0.10 ريال عند الاستبدال.</summary>
public class CustomerLoyalty : BaseEntity
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int PointsBalance { get; set; }
    public int TotalPointsEarned { get; set; }
    public int TotalPointsRedeemed { get; set; }
    /// <summary>القيمة النقدية لرصيد النقاط الحالي بالريال.</summary>
    public decimal PointsValueSar { get; set; }
    public LoyaltyTier Tier { get; set; } = LoyaltyTier.Bronze;
}
