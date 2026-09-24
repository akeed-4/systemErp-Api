namespace ERP.Core.DTOs.Shared;

public class UpgradeSubscriptionRequestDto
{
    public SubscriptionPlanId PlanId { get; set; }
    public SubscriptionBillingCycle BillingCycle { get; set; }
    public string PaymentMethod { get; set; } = "mada";
}
