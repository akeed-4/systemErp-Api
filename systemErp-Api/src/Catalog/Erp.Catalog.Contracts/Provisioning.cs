using Erp.SharedKernel.Tenancy;

namespace Erp.Catalog.Contracts;

public sealed record CompanyInfo(
    string NameAr,
    string NameEn,
    string VatNumber,
    string CrNumber,
    string City,
    string Address,
    string Phone,
    string Email,
    string? Industry);

public sealed record OwnerInfo(string Name, string Email, string Phone, string Password);

public sealed record SubscriptionRequest(string PlanCode, string BillingCycle, string PaymentMethod);

public sealed record ProvisionTenantRequest(
    string? Code,
    TenancyMode Mode,
    CompanyInfo Company,
    OwnerInfo Owner,
    SubscriptionRequest Subscription);

public sealed record ProvisionTenantResult(
    Guid TenantId,
    string TenantCode,
    Guid OwnerUserId,
    TenancyMode Mode,
    string? DatabaseName);

/// <summary>
/// Creates a tenant: catalog row (provisioning) → dedicated database create + migrate (dedicated mode only)
/// → module seeders → subscription, owner login index, schema version → active.
/// Every step is idempotent, so calling again with the same code resumes a failed provisioning.
/// </summary>
public interface ITenantProvisioningService
{
    Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Moves a tenant between the shared database and a dedicated database: copy rows by TenantId in migration order,
/// verify counts, switch the catalog row, evict caches, purge the source. Defined now; implemented in a later phase.
/// </summary>
public interface ITenantRelocationService
{
    Task RelocateToDedicatedAsync(Guid tenantId, CancellationToken cancellationToken);

    Task RelocateToSharedAsync(Guid tenantId, CancellationToken cancellationToken);
}
