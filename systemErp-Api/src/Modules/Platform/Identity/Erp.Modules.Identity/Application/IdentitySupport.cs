using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.Catalog.Contracts;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Persistence;
using Erp.SharedKernel.Messaging;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Identity.Application;

/// <summary>Section "Identity".</summary>
internal sealed class IdentityModuleOptions
{
    public const string SectionName = "Identity";

    /// <summary>Development only: return the OTP in the forgot-password response (no SMS/email provider yet).</summary>
    public bool ExposeOtpInResponse { get; set; }

    public int OtpMinutes { get; set; } = 10;

    public int MaxOtpAttempts { get; set; } = 5;
}

internal interface IOtpSender
{
    Task SendAsync(Guid userId, string? email, string? phone, string otp, CancellationToken cancellationToken);
}

/// <summary>Placeholder until an SMS/email provider is chosen. Never logs the code itself.</summary>
internal sealed class LoggingOtpSender(ILogger<LoggingOtpSender> logger) : IOtpSender
{
    public Task SendAsync(Guid userId, string? email, string? phone, string otp, CancellationToken cancellationToken)
    {
        logger.LogWarning("No OTP delivery provider configured; password-reset code for user {UserId} was not sent", userId);
        return Task.CompletedTask;
    }
}

/// <summary>Keeps catalog.TenantLoginIndex in sync with identity.Users. Written in the user's transaction, applied after commit.</summary>
internal sealed record UserLoginIndexChanged(Guid TenantId, Guid UserId, string? Email, string? Phone, bool IsActive) : IOutboxMessage
{
    public static string MessageType => "identity.user_login_index_changed";

    public static UserLoginIndexChanged From(User user, Guid tenantId) => new(tenantId, user.Id, user.Email, user.Phone, user.IsActive);
}

internal sealed class UserLoginIndexChangedHandler(ITenantLoginIndex loginIndex) : OutboxMessageHandler<UserLoginIndexChanged>
{
    protected override Task HandleAsync(UserLoginIndexChanged message, CancellationToken cancellationToken) =>
        loginIndex.UpsertAsync(new LoginIndexEntry(message.TenantId, message.UserId, message.Email, message.Phone, message.IsActive), cancellationToken);
}

/// <summary>Creates the owner user of a new tenant (idempotent; provisioning writes the owner's login-index row itself).</summary>
internal sealed class IdentitySeeder(IdentityDbContext db, IPasswordHasher<User> hasher) : IModuleSeeder
{
    public int Order => 20;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Id == context.Owner.UserId, cancellationToken))
        {
            return;
        }

        var owner = new User(context.Owner.UserId, context.Owner.Name, context.Owner.Email, context.Owner.Phone, SystemRoles.Owner);
        owner.SetPasswordHash(hasher.HashPassword(owner, context.Owner.Password));
        owner.UpdateProfile(owner.Name, owner.Phone, null, "مالك المنشأة", "الإدارة العليا");
        db.Users.Add(owner);
    }
}

/// <summary>Copies catalog roles into identity.Roles of the database being migrated.</summary>
internal sealed class RoleReferenceSeeder(IdentityDbContext db) : IReferenceDataSeeder
{
    public async Task SeedAsync(GlobalReferenceData data, CancellationToken cancellationToken)
    {
        var existing = await db.Roles.ToDictionaryAsync(r => r.Code, cancellationToken);
        foreach (var role in data.Roles)
        {
            if (!existing.TryGetValue(role.Code, out var row))
            {
                row = new Role { Code = role.Code };
                db.Roles.Add(row);
            }

            row.NameAr = role.NameAr;
            row.NameEn = role.NameEn;
            row.SortOrder = role.SortOrder;
        }
    }
}

internal sealed class UserDirectory(IdentityDbContext db) : IUserDirectory
{
    public Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSummary(u.Id, u.Name, u.Email, u.RoleCode, u.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}
