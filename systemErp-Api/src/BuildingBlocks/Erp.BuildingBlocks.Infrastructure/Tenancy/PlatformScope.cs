using Microsoft.Extensions.Logging;

namespace Erp.BuildingBlocks.Infrastructure.Tenancy;

/// <summary>
/// The only way to bypass the tenant query filter. Explicit, logged with a reason, and limited to platform work
/// (migrations, reference-data sync, outbox scanning). Lives for the rest of the DI scope once entered.
/// </summary>
public interface IPlatformScope
{
    bool IsActive { get; }

    string? Reason { get; }

    void Enter(string reason);
}

internal sealed class PlatformScope(ILogger<PlatformScope> logger) : IPlatformScope
{
    public bool IsActive { get; private set; }

    public string? Reason { get; private set; }

    public void Enter(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        IsActive = true;
        Reason = reason;
        logger.LogInformation("Platform scope entered (tenant filter bypassed): {PlatformScopeReason}", reason);
    }
}
