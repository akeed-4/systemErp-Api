using Erp.Catalog.Contracts;
using Erp.SharedKernel.Tenancy;

namespace Erp.Catalog.Persistence;

internal sealed class CatalogTenant
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public TenantStatus Status { get; set; }

    public TenancyMode TenancyMode { get; set; }

    /// <summary>Encrypted with IConnectionStringProtector; NULL for shared tenants.</summary>
    public string? ConnectionStringEncrypted { get; set; }

    public string? DatabaseName { get; set; }

    public string? SchemaVersion { get; set; }

    /// <summary>Chosen at the first provisioning attempt so retries seed the same owner.</summary>
    public Guid? OwnerUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

internal sealed class TenantLoginIndexEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string? NormalizedEmail { get; set; }

    public string? NormalizedPhone { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class SubscriptionPlan
{
    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public string DescriptionAr { get; set; } = string.Empty;

    public string DescriptionEn { get; set; } = string.Empty;

    public decimal PriceMonthly { get; set; }

    public decimal PriceYearly { get; set; }

    public bool IsPopular { get; set; }

    public string? BadgeAr { get; set; }

    public string? BadgeEn { get; set; }

    /// <summary>NULL = unlimited.</summary>
    public int? MaxUsers { get; set; }

    public int? MaxInvoicesPerMonth { get; set; }

    public int? MaxBranches { get; set; }

    public int SortOrder { get; set; }

    public List<SubscriptionPlanFeature> Features { get; set; } = [];
}

internal sealed class SubscriptionPlanFeature
{
    public int Id { get; set; }

    public string PlanCode { get; set; } = string.Empty;

    public string FeatureAr { get; set; } = string.Empty;

    public string FeatureEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

internal sealed class TenantSubscription
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public string PlanCode { get; set; } = string.Empty;

    public string BillingCycle { get; set; } = "monthly";

    public DateOnly StartDate { get; set; }

    public DateOnly ExpiryDate { get; set; }

    public string Status { get; set; } = "trial";

    public decimal PaidAmount { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;

    public string TransactionReference { get; set; } = string.Empty;

    public bool AutoRenew { get; set; }
}

internal sealed class CatalogRole
{
    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

internal sealed class CatalogScreen
{
    public string Id { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
