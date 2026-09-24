using Erp.SharedKernel.Tenancy;

namespace Erp.Catalog.Contracts;

public enum TenantStatus
{
    Active,
    Suspended,
    Provisioning,
    Archived,
}

/// <summary>Catalog metadata of a tenant. <see cref="DedicatedConnectionString"/> is already decrypted (null for shared tenants).</summary>
public sealed record TenantDescriptor(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    TenantStatus Status,
    TenancyMode Mode,
    string? DedicatedConnectionString,
    string? DatabaseName,
    string? SchemaVersion);

/// <summary>A tenant database the platform must visit (migrations, outbox): the shared DB or one dedicated DB.</summary>
public sealed record TenantDatabase(string Name, string ConnectionString, TenancyMode Mode, Guid? DedicatedTenantId);

/// <summary>Read access to catalog.Tenants, cached in memory with a short TTL.</summary>
public interface ITenantDirectory
{
    Task<TenantDescriptor?> FindAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<TenantDescriptor?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>The shared database followed by every dedicated database.</summary>
    Task<IReadOnlyList<TenantDatabase>> ListDatabasesAsync(CancellationToken cancellationToken);

    void Invalidate(Guid tenantId);
}

public sealed record LoginIndexMatch(
    Guid TenantId,
    string TenantCode,
    string TenantNameAr,
    string TenantNameEn,
    TenantStatus TenantStatus,
    Guid UserId);

public sealed record LoginIndexEntry(Guid TenantId, Guid UserId, string? Email, string? Phone, bool IsActive);

/// <summary>
/// catalog.TenantLoginIndex: maps a normalized email/phone to the tenants (and users) it belongs to,
/// so anonymous auth flows can find the tenant database before any tenant is known.
/// </summary>
public interface ITenantLoginIndex
{
    Task<IReadOnlyList<LoginIndexMatch>> FindAsync(string identifier, CancellationToken cancellationToken);

    Task<LoginIndexMatch?> FindByUserAsync(Guid userId, CancellationToken cancellationToken);

    Task UpsertAsync(LoginIndexEntry entry, CancellationToken cancellationToken);
}

/// <summary>Normalization shared by the login index writer and every lookup.</summary>
public static class LoginIdentifier
{
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();

    /// <summary>Digits only; a leading Saudi 05xxxxxxxx becomes 9665xxxxxxxx so both spellings match.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal))
        {
            digits = "966" + digits[1..];
        }

        return digits.Length == 0 ? null : digits;
    }

    public static bool LooksLikeEmail(string identifier) => identifier.Contains('@', StringComparison.Ordinal);
}
