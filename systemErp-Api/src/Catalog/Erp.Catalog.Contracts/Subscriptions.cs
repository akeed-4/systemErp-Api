namespace Erp.Catalog.Contracts;

/// <summary>Frontend SubscriptionPlan shape. Limits are a number or the string "unlimited".</summary>
public sealed record SubscriptionPlanDto(
    string Id,
    string NameAr,
    string NameEn,
    string DescriptionAr,
    string DescriptionEn,
    decimal PriceMonthly,
    decimal PriceYearly,
    bool IsPopular,
    string? BadgeAr,
    string? BadgeEn,
    object MaxUsers,
    object MaxInvoicesPerMonth,
    object Branches,
    IReadOnlyList<string> FeaturesAr,
    IReadOnlyList<string> FeaturesEn);

/// <summary>Frontend CompanySubscription shape.</summary>
public sealed record CompanySubscriptionDto(
    string PlanId,
    string PlanNameAr,
    string PlanNameEn,
    string BillingCycle,
    DateOnly StartDate,
    DateOnly ExpiryDate,
    string Status,
    decimal PaidAmount,
    string PaymentMethod,
    string TransactionReference);

public interface ISubscriptionCatalog
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken);

    Task<CompanySubscriptionDto?> GetCurrentAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Replaces the tenant's current subscription with an active one on <paramref name="planCode"/>.</summary>
    Task<CompanySubscriptionDto> UpgradeAsync(Guid tenantId, string planCode, string billingCycle, string paymentMethod, CancellationToken cancellationToken);
}
