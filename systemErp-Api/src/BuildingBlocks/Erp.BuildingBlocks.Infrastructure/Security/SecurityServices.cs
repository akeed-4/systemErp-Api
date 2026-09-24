using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Erp.BuildingBlocks.Infrastructure.Security;

/// <summary>The caller outside HTTP (migrator, background jobs): not authenticated.</summary>
internal sealed class SystemCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public Guid? UserId => null;

    public string? Role => null;

    public string? Email => null;

    public string? Name => null;
}

/// <summary>
/// Encrypts dedicated-tenant connection strings at rest in the catalog.
/// Swap the implementation for Azure Key Vault/KMS without touching callers.
/// </summary>
public interface IConnectionStringProtector
{
    string Protect(string connectionString);

    string Unprotect(string protectedConnectionString);
}

/// <summary>Encrypts tenant secrets at rest (ZATCA CSID/secret/private key, integration keys). Key Vault-ready like the connection-string protector.</summary>
public interface ISecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Erp.Tenant.Secrets.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

internal sealed class DataProtectionConnectionStringProtector(IDataProtectionProvider provider) : IConnectionStringProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Erp.Catalog.TenantConnectionString.v1");

    public string Protect(string connectionString) => _protector.Protect(connectionString);

    public string Unprotect(string protectedConnectionString) => _protector.Unprotect(protectedConnectionString);
}
