namespace ERP.Core.DTOs.Shared;

/// <summary>يطابق CompanyRegistrationRequest في erp.models.ts.</summary>
public class CompanyRegistrationRequestDto
{
    public string CompanyNameAr { get; set; } = string.Empty;
    public string CompanyNameEn { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string CrNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;

    public SubscriptionPlanId PlanId { get; set; } = SubscriptionPlanId.Professional;
    public SubscriptionBillingCycle BillingCycle { get; set; } = SubscriptionBillingCycle.Yearly;
    /// <summary>mada | credit_card | bank_transfer | apple_pay</summary>
    public string PaymentMethod { get; set; } = "mada";

    public string AdminName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPhone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
