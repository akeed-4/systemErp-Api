namespace Erp.BuildingBlocks.Infrastructure.Modules;

public sealed record CompanySeed(
    string NameAr,
    string NameEn,
    string VatNumber,
    string CrNumber,
    string City,
    string Address,
    string Country,
    string Phone,
    string Email,
    string? Industry);

public sealed record OwnerSeed(Guid UserId, string Name, string Email, string Phone, string Password);

/// <summary>Everything a module needs to seed a new tenant. Runs inside a scope bound to that tenant.</summary>
public sealed record TenantSeedContext(
    Guid TenantId,
    string TenantCode,
    CompanySeed Company,
    OwnerSeed Owner,
    string BaseCurrencyCode);

/// <summary>
/// Seeds one module's data for a new tenant (company profile, head office, owner, role permissions, …).
/// Must be idempotent: provisioning retries re-run every seeder. Stage changes only; the caller saves.
/// </summary>
public interface IModuleSeeder
{
    int Order { get; }

    Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken);
}

public sealed record RoleDefinition(string Code, string NameAr, string NameEn, int SortOrder);

public sealed record ScreenDefinition(string Id, string NameAr, string NameEn, int SortOrder);

/// <summary>Global reference data owned by the catalog and copied read-only into every tenant database.</summary>
public sealed record GlobalReferenceData(IReadOnlyList<RoleDefinition> Roles, IReadOnlyList<ScreenDefinition> Screens);

public interface IGlobalReferenceDataSource
{
    Task<GlobalReferenceData> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Upserts a module's copy of the global reference data into the database of the current platform scope.</summary>
public interface IReferenceDataSeeder
{
    Task SeedAsync(GlobalReferenceData data, CancellationToken cancellationToken);
}
