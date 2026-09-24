using Erp.Catalog.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Modules.Identity.Domain;

/// <summary>identity.Roles: read-only copy of catalog.Roles (global reference data, no TenantId).</summary>
[GlobalReferenceData]
internal sealed class Role
{
    public string Code { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

internal sealed class User : TenantEntity
{
    private User()
    {
    }

    public User(Guid id, string name, string email, string? phone, string roleCode)
    {
        Id = id;
        Name = name.Trim();
        ChangeEmail(email);
        ChangePhone(phone);
        RoleCode = roleCode;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public string? Phone { get; private set; }

    public string? NormalizedPhone { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    public string RoleCode { get; private set; } = string.Empty;

    public string? JobTitle { get; private set; }

    public string? Department { get; private set; }

    public string? AvatarUrl { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public string AvatarInitials =>
        string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0])).ToUpperInvariant();

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void ChangeEmail(string? email)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        NormalizedEmail = LoginIdentifier.NormalizeEmail(email);
    }

    public void ChangePhone(string? phone)
    {
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        NormalizedPhone = LoginIdentifier.NormalizePhone(phone);
    }

    public void UpdateProfile(string name, string? phone, string? avatarUrl, string? jobTitle, string? department)
    {
        Name = name.Trim();
        ChangePhone(phone);
        AvatarUrl = avatarUrl;
        JobTitle = jobTitle;
        Department = department;
    }

    public void ChangeRole(string roleCode) => RoleCode = roleCode;

    public void SetActive(bool isActive) => IsActive = isActive;

    public void RecordLogin(DateTimeOffset at) => LastLoginAt = at;
}

internal sealed class RefreshToken : TenantEntity
{
    private RefreshToken()
    {
    }

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}

internal sealed class PasswordResetRequest : TenantEntity
{
    private PasswordResetRequest()
    {
    }

    public PasswordResetRequest(Guid userId, string otpHash, DateTimeOffset expiresAt)
    {
        UserId = userId;
        OtpHash = otpHash;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public string OtpHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public string? ResetTokenHash { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsOpen(DateTimeOffset now, int maxAttempts) => ConsumedAt is null && ExpiresAt > now && Attempts < maxAttempts;

    public void RegisterFailedAttempt() => Attempts++;

    public void MarkVerified(DateTimeOffset at, string resetTokenHash)
    {
        VerifiedAt = at;
        ResetTokenHash = resetTokenHash;
    }

    public void Consume(DateTimeOffset at) => ConsumedAt = at;
}
