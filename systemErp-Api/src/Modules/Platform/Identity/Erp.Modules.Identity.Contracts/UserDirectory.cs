namespace Erp.Modules.Identity.Contracts;

public sealed record UserSummary(Guid Id, string Name, string? Email, string Role, bool IsActive);

/// <summary>Read-only user lookup for other modules (current tenant only).</summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);
}
