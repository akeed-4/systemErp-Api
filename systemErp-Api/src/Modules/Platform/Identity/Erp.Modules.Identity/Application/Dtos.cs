using Erp.Modules.Identity.Domain;
using Erp.Modules.Organization.Contracts;

namespace Erp.Modules.Identity.Application;

/// <summary>The frontend User shape.</summary>
internal sealed record UserDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Email,
    string? Phone,
    string Role,
    string AvatarInitials,
    string? AvatarUrl,
    string? JobTitle,
    string? Department,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt)
{
    public static UserDto From(User u) =>
        new(u.Id, u.TenantId, u.Name, u.Email, u.Phone, u.RoleCode, u.AvatarInitials, u.AvatarUrl, u.JobTitle, u.Department, u.IsActive, u.CreatedAt, u.LastLoginAt);
}

internal sealed record TenantChoiceDto(Guid Id, string Code, string NameAr, string NameEn);

/// <summary>
/// Response of login / refresh / tenant switch. Keeps the shape the frontend already consumes
/// ({success, message, token, user, tenant}); tenant selection adds requiresTenantSelection + tenants.
/// </summary>
internal sealed record AuthResponse(
    bool Success,
    string? Message,
    string? Token = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    UserDto? User = null,
    CompanyProfileDto? Tenant = null,
    bool? RequiresTenantSelection = null,
    IReadOnlyList<TenantChoiceDto>? Tenants = null);

internal sealed record RegisterCompanyResponse(
    bool Success,
    Guid TenantId,
    string Message,
    string? Token,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    UserDto? User,
    CompanyProfileDto? Tenant);

internal sealed record LoginRequest(string? Email, string? Identifier, string? Password, string? TenantCode);

internal sealed record RefreshRequest(string? RefreshToken);

internal sealed record ForgotPasswordRequest(string? Identifier, string? TenantCode);

internal sealed record ForgotPasswordResponse(
    bool Success,
    string? Message,
    Guid? UserId = null,
    string? UserName = null,
    string? MaskedEmail = null,
    string? MaskedPhone = null,
    string? OtpCode = null,
    int? ExpiresInSeconds = null,
    bool? RequiresTenantSelection = null,
    IReadOnlyList<TenantChoiceDto>? Tenants = null);

internal sealed record VerifyOtpRequest(Guid UserId, string? Otp);

internal sealed record VerifyOtpResponse(bool Success, string? Message, string? ResetToken);

internal sealed record ResetPasswordRequest(Guid UserId, string? Otp, string? ResetToken, string? NewPassword);

internal sealed record SimpleResponse(bool Success, string? Message);

/// <summary>The frontend CompanyRegistrationRequest.</summary>
internal sealed record CompanyRegistrationRequest(
    string? CompanyNameAr,
    string? CompanyNameEn,
    string? VatNumber,
    string? CrNumber,
    string? City,
    string? Address,
    string? Phone,
    string? Email,
    string? Industry,
    string? PlanId,
    string? BillingCycle,
    string? PaymentMethod,
    string? AdminName,
    string? AdminEmail,
    string? AdminPhone,
    string? Password);

internal sealed record CreateUserRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Role,
    string? Password,
    string? JobTitle,
    string? Department);

internal sealed record UpdateUserRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Role,
    string? JobTitle,
    string? Department,
    string? AvatarUrl,
    bool? IsActive);

internal sealed record UpdateMyProfileRequest(string? Name, string? Phone, string? AvatarUrl, string? JobTitle, string? Department);
