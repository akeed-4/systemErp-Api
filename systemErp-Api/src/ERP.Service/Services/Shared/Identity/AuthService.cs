using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Service.Services.Shared;

public class AuthService : IAuthService
{
    private const int OtpMinutes = 10;
    private const int MaxOtpAttempts = 5;
    private const int MinPasswordLength = 8;

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ITenantProvisioningService _provisioning;
    private readonly ITransactionRunner _tx;
    private readonly IJwtTokenService _tokens;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _log;

    public AuthService(ErpDbContext db, ITenantContext tenant, ITenantProvisioningService provisioning, ITransactionRunner tx,
        IJwtTokenService tokens, IPasswordHasher<User> hasher, IConfiguration config, ILogger<AuthService> log)
    {
        _db = db; _tenant = tenant; _provisioning = provisioning; _tx = tx;
        _tokens = tokens; _hasher = hasher; _config = config; _log = log;
    }

    public async Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new ValidationFailedException("البريد الإلكتروني وكلمة المرور مطلوبان.");

        var email = request.Email.Trim().ToLower();

        // الدخول يسبق معرفة المنشأة، لذلك يُتجاوز مرشّح المنشأة هنا فقط ويُستنتج المستأجر من المستخدم.
        var candidates = await _db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.Email.ToLower() == email && u.IsActive).ToListAsync(ct);

        User? user = null;
        foreach (var c in candidates.OrderByDescending(c => c.LastLoginAt))
        {
            if (c.PasswordHash != null
                && _hasher.VerifyHashedPassword(c, c.PasswordHash, request.Password) != PasswordVerificationResult.Failed)
            {
                user = c;
                break;
            }
        }
        if (user == null) throw new UnauthorizedAppException();

        _tenant.SetTenant(user.TenantId);
        var tenant = await _db.Set<Tenant>().FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is not { IsActive: true }) throw new ForbiddenException("المنشأة غير مفعّلة.");

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Build(user, tenant, "تم تسجيل الدخول بنجاح");
    }

    public Task<AuthResultDto> RegisterCompanyAsync(CompanyRegistrationRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            ValidateRegistration(r);
            var email = r.AdminEmail.Trim().ToLower();

            if (await _db.Set<Tenant>().IgnoreQueryFilters().AnyAsync(t => t.VatNumber == r.VatNumber, token))
                throw new ConflictException("هذا الرقم الضريبي مسجّل مسبقاً.");
            if (await _db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == email, token))
                throw new ConflictException("هذا البريد الإلكتروني مسجّل مسبقاً.");

            var tenant = new Tenant
            {
                Code = $"T-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                NameAr = r.CompanyNameAr.Trim(),
                NameEn = string.IsNullOrWhiteSpace(r.CompanyNameEn) ? r.CompanyNameAr.Trim() : r.CompanyNameEn.Trim(),
                VatNumber = r.VatNumber,
                CrNumber = r.CrNumber,
                Address = r.Address,
                City = string.IsNullOrWhiteSpace(r.City) ? "Riyadh" : r.City,
                Phone = r.Phone,
                Email = string.IsNullOrWhiteSpace(r.Email) ? email : r.Email,
            };
            tenant.Id = Guid.NewGuid();
            tenant.TenantId = tenant.Id;
            _tenant.SetTenant(tenant.Id);
            _db.Add(tenant);

            var plan = SubscriptionCatalog.Get(r.PlanId);
            _db.Add(SubscriptionCatalog.NewSubscription(plan, r.BillingCycle, r.PaymentMethod));

            var admin = new User
            {
                Name = r.AdminName.Trim(),
                Email = email,
                Phone = r.AdminPhone,
                Role = UserRole.Owner,
                AvatarInitials = Initials(r.AdminName),
                LastLoginAt = DateTime.UtcNow,
            };
            admin.PasswordHash = _hasher.HashPassword(admin, r.Password);
            _db.Add(admin);

            await _db.SaveChangesAsync(token);
            await _provisioning.SeedAsync(tenant.Id, tenant.Currency, token);

            return Build(admin, tenant, $"تم تسجيل منشأة '{tenant.NameAr}' وتفعيل الاشتراك بنجاح!");
        }, ct);

    private static void ValidateRegistration(CompanyRegistrationRequestDto r)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.CompanyNameAr)) errors.Add("اسم المنشأة بالعربية مطلوب.");
        if (!SaudiVat.IsValid(r.VatNumber))
            errors.Add("الرقم الضريبي السعودي يجب أن يتكون من 15 خانة ويبدأ وينتهي بالرقم 3.");
        if (string.IsNullOrWhiteSpace(r.AdminName)) errors.Add("اسم المدير مطلوب.");
        if (string.IsNullOrWhiteSpace(r.AdminEmail) || !r.AdminEmail.Contains('@')) errors.Add("بريد المدير غير صالح.");
        if (r.Password is null || r.Password.Length < MinPasswordLength) errors.Add($"كلمة المرور {MinPasswordLength} أحرف على الأقل.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    private AuthResultDto Build(User user, Tenant tenant, string message)
    {
        var (token, expires) = _tokens.Create(user);
        return new AuthResultDto
        {
            Success = true,
            Message = message,
            Token = token,
            ExpiresIn = (int)(expires - DateTime.UtcNow).TotalSeconds,
            TenantId = tenant.Id,
            User = Mapper.Map<UserDto>(user),
            Tenant = Mapper.Map<TenantDto>(tenant),
        };
    }

    // ---------------- استعادة كلمة المرور ----------------
    public async Task<ForgotPasswordResultDto> RequestPasswordResetAsync(ForgotPasswordRequestDto request, CancellationToken ct = default)
    {
        var id = request.Identifier?.Trim().ToLower() ?? string.Empty;
        if (id.Length == 0) throw new ValidationFailedException("أدخل البريد الإلكتروني أو رقم الجوال.");

        var user = await _db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.IsActive && (u.Email.ToLower() == id || u.Phone == id)).FirstOrDefaultAsync(ct);

        // لا نكشف وجود الحساب من عدمه: الاستجابة نفسها في الحالتين، مع رمز فقط للحساب الموجود.
        var result = new ForgotPasswordResultDto
        {
            Success = true,
            Message = "إن كان الحساب موجوداً فقد أُرسل رمز التحقق.",
            ExpiresInSeconds = OtpMinutes * 60,
        };
        if (user == null) return result;

        _tenant.SetTenant(user.TenantId);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        // إبطال الرموز السابقة غير المستخدمة
        foreach (var old in await _db.Set<PasswordResetOtp>().Where(o => o.UserId == user.Id && o.UsedAt == null).ToListAsync(ct))
            old.UsedAt = DateTime.UtcNow;

        _db.Add(new PasswordResetOtp { UserId = user.Id, CodeHash = HashOtp(user.Id, code), ExpiresAt = DateTime.UtcNow.AddMinutes(OtpMinutes) });
        await _db.SaveChangesAsync(ct);

        result.UserId = user.Id;
        result.UserName = user.Name;
        result.MaskedEmail = MaskEmail(user.Email);
        result.MaskedPhone = string.IsNullOrEmpty(user.Phone) ? null : MaskPhone(user.Phone);

        if (_config.GetValue<bool>("Auth:ExposeOtpInResponse"))
            result.OtpCode = code; // للتطوير فقط
        else
            _log.LogWarning("لم يُضبط مزوّد إرسال OTP؛ الرمز لم يُرسل للمستخدم {UserId}.", user.Id);

        return result;
    }

    public async Task<VerifyOtpResultDto> VerifyResetOtpAsync(VerifyOtpRequestDto request, CancellationToken ct = default)
    {
        var otp = await FindValidOtpAsync(request.UserId, request.Otp, markAttempt: true, ct);
        otp.VerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new VerifyOtpResultDto { Success = true, Message = "تم التحقق من الرمز بنجاح.", ResetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) };
    }

    public async Task<OperationResultDto> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default)
    {
        if (request.NewPassword is null || request.NewPassword.Trim().Length < MinPasswordLength)
            throw new ValidationFailedException($"كلمة المرور {MinPasswordLength} أحرف على الأقل.");

        var otp = await FindValidOtpAsync(request.UserId, request.Otp, markAttempt: true, ct);
        var user = await _db.Set<User>().FirstAsync(u => u.Id == request.UserId, ct);

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword.Trim());
        otp.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new OperationResultDto { Message = "تم تغيير كلمة المرور بنجاح." };
    }

    private async Task<PasswordResetOtp> FindValidOtpAsync(Guid userId, string code, bool markAttempt, CancellationToken ct)
    {
        var user = await _db.Set<User>().IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new ValidationFailedException("رمز التحقق غير صالح أو منتهي.");
        _tenant.SetTenant(user.TenantId);

        var otp = await _db.Set<PasswordResetOtp>().Where(o => o.UserId == userId && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (otp == null || otp.ExpiresAt < DateTime.UtcNow || otp.Attempts >= MaxOtpAttempts)
            throw new ValidationFailedException("رمز التحقق غير صالح أو منتهي.");

        if (markAttempt) otp.Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(HashOtp(userId, code?.Trim() ?? string.Empty))))
        {
            await _db.SaveChangesAsync(ct);
            throw new ValidationFailedException("رمز التحقق غير صحيح.");
        }
        return otp;
    }

    private string HashOtp(Guid userId, string code)
    {
        var pepper = _config["Jwt:Key"] ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}:{pepper}")));
    }

    internal static string Initials(string name)
        => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => p[0]));

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}***{email[(at - 1)..]}";
    }

    private static string MaskPhone(string phone) => phone.Length <= 4 ? phone : $"{new string('*', phone.Length - 3)}{phone[^3..]}";
}
