using System.Globalization;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Erp.SharedKernel.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Identity.Application;

/// <summary>
/// Auth flows that start before the tenant is known (login, refresh, forgot password, register) or that target another
/// tenant (switch). The tenant is found through catalog.TenantLoginIndex; each tenant is then handled in its own child
/// scope, so no scope or transaction ever spans two tenants.
/// </summary>
internal sealed class AuthService(
    ITenantLoginIndex loginIndex,
    ITenantScopeFactory scopes,
    ITenantProvisioningService provisioning,
    IOtpSender otpSender,
    ILogger<AuthService> logger)
{
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var identifier = (request.Email ?? request.Identifier)?.Trim();
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrEmpty(request.Password))
        {
            throw ErpException.Validation("Email/phone and password are required.", "البريد الإلكتروني/الجوال وكلمة المرور مطلوبة.");
        }

        var candidates = FilterByCode(await loginIndex.FindAsync(identifier, ct), request.TenantCode);
        EnsureSomeTenantIsActive(candidates, request.TenantCode);

        // The password is checked in every candidate tenant BEFORE listing tenants, so the choice list never
        // reveals memberships to someone who does not know the password.
        var valid = new List<LoginIndexMatch>();
        foreach (var match in candidates.Where(m => m.TenantStatus == TenantStatus.Active))
        {
            try
            {
                await using var scope = await scopes.CreateForTenantAsync(match.TenantId, requireServable: true, ct);
                var session = scope.ServiceProvider.GetRequiredService<TenantAuthSession>();
                var user = await session.FindActiveUserAsync(match.UserId, ct);
                if (user is not null && session.VerifyPassword(user, request.Password))
                {
                    valid.Add(match);
                }
            }
            catch (ErpException ex) when (candidates.Count > 1)
            {
                logger.LogWarning("Skipping tenant {TenantCode} during login: {Reason}", match.TenantCode, ex.Code);
            }
        }

        if (valid.Count == 0)
        {
            throw AuthErrors.InvalidCredentials();
        }

        if (valid.Count > 1)
        {
            return new AuthResponse(
                false,
                "هذا الحساب مرتبط بأكثر من منشأة، يرجى اختيار المنشأة للمتابعة.",
                RequiresTenantSelection: true,
                Tenants: valid.Select(ToChoice).ToList());
        }

        return await SignInAsync(valid[0].TenantId, valid[0].UserId, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var tenantId = TokenService.ReadTenantId(request.RefreshToken) ?? throw AuthErrors.InvalidRefreshToken();
        await using var scope = await scopes.CreateForTenantAsync(tenantId, requireServable: true, ct);
        return await scope.ServiceProvider.GetRequiredService<TenantAuthSession>().RefreshAsync(request.RefreshToken!, ct);
    }

    public async Task<ForgotPasswordResponse> RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier))
        {
            throw ErpException.Validation("Email or phone is required.", "البريد الإلكتروني أو الجوال مطلوب.");
        }

        var candidates = FilterByCode(await loginIndex.FindAsync(request.Identifier, ct), request.TenantCode)
            .Where(m => m.TenantStatus == TenantStatus.Active)
            .ToList();
        if (candidates.Count == 0)
        {
            throw AuthErrors.AccountNotFound();
        }

        if (candidates.Count > 1)
        {
            return new ForgotPasswordResponse(
                false,
                "هذا الحساب مرتبط بأكثر من منشأة، يرجى اختيار المنشأة.",
                RequiresTenantSelection: true,
                Tenants: candidates.Select(ToChoice).ToList());
        }

        await using var scope = await scopes.CreateForTenantAsync(candidates[0].TenantId, requireServable: true, ct);
        return await scope.ServiceProvider.GetRequiredService<TenantAuthSession>()
            .StartPasswordResetAsync(candidates[0].UserId, otpSender, ct);
    }

    public async Task<VerifyOtpResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Otp))
        {
            throw AuthErrors.InvalidOtp();
        }

        await using var scope = await OpenUserTenantAsync(request.UserId, ct);
        return await scope.ServiceProvider.GetRequiredService<TenantAuthSession>().VerifyOtpAsync(request.UserId, request.Otp, ct);
    }

    public async Task<SimpleResponse> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        PasswordRules.Validate(request.NewPassword, "newPassword");
        await using var scope = await OpenUserTenantAsync(request.UserId, ct);
        await scope.ServiceProvider.GetRequiredService<TenantAuthSession>()
            .ResetPasswordAsync(request.UserId, request.Otp, request.ResetToken, request.NewPassword!, ct);
        return new SimpleResponse(true, "تم تحديث كلمة المرور بنجاح! يمكنك تسجيل الدخول الآن.");
    }

    public async Task<RegisterCompanyResponse> RegisterCompanyAsync(CompanyRegistrationRequest request, CancellationToken ct)
    {
        ValidateRegistration(request);
        var result = await provisioning.ProvisionAsync(
            new ProvisionTenantRequest(
                Code: null,
                TenancyMode.Shared,
                new CompanyInfo(
                    request.CompanyNameAr ?? request.CompanyNameEn!,
                    request.CompanyNameEn ?? request.CompanyNameAr!,
                    request.VatNumber!,
                    request.CrNumber ?? string.Empty,
                    request.City ?? string.Empty,
                    request.Address ?? string.Empty,
                    request.Phone ?? string.Empty,
                    request.Email ?? request.AdminEmail!,
                    request.Industry),
                new OwnerInfo(request.AdminName!, request.AdminEmail!, request.AdminPhone ?? request.Phone ?? string.Empty, request.Password!),
                new SubscriptionRequest(request.PlanId ?? "starter", request.BillingCycle ?? "monthly", request.PaymentMethod ?? "mada")),
            ct);

        var signIn = await SignInAsync(result.TenantId, result.OwnerUserId, ct);
        return new RegisterCompanyResponse(
            true,
            result.TenantId,
            "تم تسجيل المنشأة وتفعيل الحساب بنجاح.",
            signIn.Token,
            signIn.RefreshToken,
            signIn.ExpiresAt,
            signIn.User,
            signIn.Tenant);
    }

    /// <summary>Issues a token for <paramref name="targetTenantId"/> if the signed-in person (same email) is an active user there.</summary>
    public async Task<AuthResponse> SwitchTenantAsync(Guid targetTenantId, ICurrentUser currentUser, CancellationToken ct)
    {
        var email = currentUser.Email ?? throw ErpException.Forbidden(
            "tenant_switch_unavailable", "Switching company requires an account with an email.", "التبديل بين المنشآت يتطلب حساباً ببريد إلكتروني.");

        var match = (await loginIndex.FindAsync(email, ct)).FirstOrDefault(m => m.TenantId == targetTenantId)
            ?? throw ErpException.Forbidden("tenant_access_denied", "You have no account in that company.", "ليس لديك حساب في هذه المنشأة.");

        return await SignInAsync(match.TenantId, match.UserId, ct);
    }

    private async Task<AuthResponse> SignInAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var scope = await scopes.CreateForTenantAsync(tenantId, requireServable: true, ct);
        var session = scope.ServiceProvider.GetRequiredService<TenantAuthSession>();
        return await session.SignInAsync(userId, "تم تسجيل الدخول بنجاح! مرحباً بك.", ct);
    }

    private async Task<AsyncServiceScope> OpenUserTenantAsync(Guid userId, CancellationToken ct)
    {
        var match = await loginIndex.FindByUserAsync(userId, ct) ?? throw AuthErrors.InvalidOtp();
        return await scopes.CreateForTenantAsync(match.TenantId, requireServable: true, ct);
    }

    private static List<LoginIndexMatch> FilterByCode(IReadOnlyList<LoginIndexMatch> matches, string? tenantCode) =>
        string.IsNullOrWhiteSpace(tenantCode)
            ? matches.ToList()
            : matches.Where(m => string.Equals(m.TenantCode, tenantCode.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private static void EnsureSomeTenantIsActive(List<LoginIndexMatch> candidates, string? tenantCode)
    {
        if (candidates.Count > 0 && candidates.TrueForAll(m => m.TenantStatus != TenantStatus.Active) && !string.IsNullOrWhiteSpace(tenantCode))
        {
            throw ErpException.Forbidden("tenant_not_active", "The company account is not active.", "حساب المنشأة غير مفعّل حالياً.");
        }
    }

    private static TenantChoiceDto ToChoice(LoginIndexMatch m) => new(m.TenantId, m.TenantCode, m.TenantNameAr, m.TenantNameEn);

    private static void ValidateRegistration(CompanyRegistrationRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.CompanyNameAr) && string.IsNullOrWhiteSpace(request.CompanyNameEn))
        {
            errors["companyNameAr"] = ["The company name is required."];
        }

        if (!SaudiIdentifiers.IsVatNumber(request.VatNumber))
        {
            errors["vatNumber"] = ["The VAT number must be 15 digits starting and ending with 3."];
        }

        if (string.IsNullOrWhiteSpace(request.AdminName))
        {
            errors["adminName"] = ["The administrator name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.AdminEmail) || !request.AdminEmail.Contains('@', StringComparison.Ordinal))
        {
            errors["adminEmail"] = ["A valid administrator email is required."];
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("The company registration is not valid.", "بيانات تسجيل المنشأة غير مكتملة أو غير صحيحة.", errors);
        }

        PasswordRules.Validate(request.Password, "password");
    }
}

internal static class PasswordRules
{
    public const int MinLength = 8;

    public static void Validate(string? password, string field)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
        {
            throw ErpException.Validation(
                $"The password must be at least {MinLength.ToString(CultureInfo.InvariantCulture)} characters.",
                $"يجب ألا تقل كلمة المرور عن {MinLength.ToString(CultureInfo.InvariantCulture)} أحرف.",
                new Dictionary<string, string[]> { [field] = ["too_short"] });
        }
    }
}
