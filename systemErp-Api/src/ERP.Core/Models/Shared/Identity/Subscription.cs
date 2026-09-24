
namespace ERP.Core.Models.Shared;

public class Subscription : BaseEntity
{
    public SubscriptionPlanId PlanType { get; set; }
    public string PlanNameAr { get; set; } = string.Empty;
    public string PlanNameEn { get; set; } = string.Empty;
    public SubscriptionBillingCycle BillingCycle { get; set; }
    public decimal Price { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public SubscriptionStatus Status { get; set; }
    public string PaymentMethod { get; set; } = "Mada";
    public string TransactionReference { get; set; } = string.Empty;
    public bool AutoRenew { get; set; } = true;
    public int MaxUsers { get; set; } = 10;
    public int MaxBranches { get; set; } = 3;
    public bool ZatcaPhase2Enabled { get; set; } = true;
}
