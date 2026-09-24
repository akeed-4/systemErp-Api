using System.Security.Cryptography;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Persistence;
using Erp.Modules.Organization.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Application;

/// <summary>
/// Auth operations inside ONE tenant's database. Always resolved from a scope bound to that tenant
/// (the request scope, or a child scope opened by <see cref="AuthService"/>).
/// </summary>
internal sealed class TenantAuthSession(
    IdentityDbContext db,
    IPasswordHasher<User> hasher,
    TokenService tokens,
    ICompanyProfileReader companies,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    IOptions<IdentityModuleOptions> options,
    TimeProvider clock)
{
    public Task<User?> FindActiveUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);

    public bool VerifyPassword(User user, string password)
    {
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return false;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, password));
        }

        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public async Task<AuthResponse> SignInAsync(Guid userId, string message, CancellationToken ct)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw AuthErrors.InvalidCredentials();
        user.RecordLogin(clock.GetUtcNow());
        var issued = tokens.Issue(user, tenant.TenantId, tenant.TenantCode);
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildResponseAsync(user, issued, message, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || !stored.IsUsable(now))
        {
            throw AuthErrors.InvalidRefreshToken();
        }

        var user = await FindActiveUserAsync(stored.UserId, ct) ?? throw AuthErrors.InvalidRefreshToken();
        stored.Revoke(now);
        var issued = tokens.Issue(user, tenant.TenantId, tenant.TenantCode);
        await unitOfWork.SaveChangesAsync(ct);
        return await BuildResponseAsync(user, issued, "تم تجديد الجلسة", ct);
    }

    public async Task RevokeAsync(Guid userId, string? refreshToken, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = TokenService.Hash(refreshToken);
            query = query.Where(t => t.TokenHash == hash);
        }

        foreach (var token in await query.ToListAsync(ct))
        {
            token.Revoke(now);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<ForgotPasswordResponse> StartPasswordResetAsync(Guid userId, IOtpSender sender, CancellationToken ct)
    {
        var user = await FindActiveUserAsync(userId, ct) ?? throw AuthErrors.AccountNotFound();
        var now = clock.GetUtcNow();
        var settings = options.Value;

        foreach (var open in await db.PasswordResetRequests.Where(r => r.UserId == userId && r.ConsumedAt == null).ToListAsync(ct))
        {
            open.Consume(now);
        }

        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        db.PasswordResetRequests.Add(new PasswordResetRequest(userId, OtpHash(userId, otp), now.AddMinutes(settings.OtpMinutes)));
        await unitOfWork.SaveChangesAsync(ct);
        await sender.SendAsync(user.Id, user.Email, user.Phone, otp, ct);

        return new ForgotPasswordResponse(
            true,
            "تم إرسال رمز التحقق بنجاح",
            UserId: user.Id,
            UserName: user.Name,
            MaskedEmail: Mask.Email(user.Email),
            MaskedPhone: Mask.Phone(user.Phone),
            OtpCode: settings.ExposeOtpInResponse ? otp : null,
            ExpiresInSeconds: settings.OtpMinutes * 60);
    }

    public async Task<VerifyOtpResponse> VerifyOtpAsync(Guid userId, string otp, CancellationToken ct)
    {
        var request = await OpenRequestAsync(userId, ct);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(request.OtpHash), Convert.FromHexString(OtpHash(userId, otp))))
        {
            request.RegisterFailedAttempt();
            await unitOfWork.SaveChangesAsync(ct);
            throw AuthErrors.InvalidOtp();
        }

        var resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        request.MarkVerified(clock.GetUtcNow(), TokenService.Hash(resetToken));
        await unitOfWork.SaveChangesAsync(ct);
        return new VerifyOtpResponse(true, "تم التحقق من الرمز بنجاح.", resetToken);
    }

    public async Task ResetPasswordAsync(Guid userId, string? otp, string? resetToken, string newPassword, CancellationToken ct)
    {
        var request = await OpenRequestAsync(userId, ct);
        var tokenOk = !string.IsNullOrWhiteSpace(resetToken) && request.ResetTokenHash == TokenService.Hash(resetToken);
        var otpOk = !string.IsNullOrWhiteSpace(otp) && request.OtpHash == OtpHash(userId, otp);
        if (!tokenOk && !otpOk)
        {
            request.RegisterFailedAttempt();
            await unitOfWork.SaveChangesAsync(ct);
            throw AuthErrors.InvalidOtp();
        }

        var user = await FindActiveUserAsync(userId, ct) ?? throw AuthErrors.AccountNotFound();
        user.SetPasswordHash(hasher.HashPassword(user, newPassword));
        var now = clock.GetUtcNow();
        request.Consume(now);
        foreach (var token in await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct))
        {
            token.Revoke(now);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<AuthResponse> BuildResponseAsync(User user, IssuedTokens issued, string message, CancellationToken ct) =>
        new(
            true,
            message,
            issued.AccessToken,
            issued.RefreshToken,
            issued.ExpiresAt,
            UserDto.From(user),
            await companies.GetCurrentAsync(ct));

    private async Task<PasswordResetRequest> OpenRequestAsync(Guid userId, CancellationToken ct)
    {
        var request = await db.PasswordResetRequests
            .Where(r => r.UserId == userId && r.ConsumedAt == null)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return request is not null && request.IsOpen(clock.GetUtcNow(), options.Value.MaxOtpAttempts)
            ? request
            : throw AuthErrors.InvalidOtp();
    }

    private static string OtpHash(Guid userId, string otp) => TokenService.Hash($"{userId:N}:{otp.Trim()}");
}

internal static class AuthErrors
{
    public static ErpException InvalidCredentials() => ErpException.Unauthorized(
        "invalid_credentials",
        "The email/phone or password is incorrect.",
        "البريد الإلكتروني/الجوال أو كلمة المرور غير صحيحة.");

    public static ErpException InvalidRefreshToken() => ErpException.Unauthorized(
        "invalid_refresh_token", "The session has expired. Please sign in again.", "انتهت صلاحية الجلسة، يرجى تسجيل الدخول مجدداً.");

    public static ErpException AccountNotFound() => ErpException.NotFound("Account", "الحساب");

    public static ErpException InvalidOtp() => ErpException.Validation(
        "The verification code is invalid or has expired.", "رمز التحقق غير صحيح أو منتهي الصلاحية.");
}

internal static class Mask
{
    public static string? Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        var local = email[..at];
        return (local.Length <= 2 ? local[..1] : local[..2]) + "***" + email[at..];
    }

    public static string? Phone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) || phone.Length < 4 ? null : new string('*', phone.Length - 3) + phone[^3..];
}
