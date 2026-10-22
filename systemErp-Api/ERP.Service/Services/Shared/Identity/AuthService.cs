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
    /// <summary>مهلة بين طلبَي رمز لنفس المستخدم: تمنع إغراق بريده بالرسائل.</summary>
    private const int OtpResendSeconds = 60;
    private const int MinPasswordLength = 8;

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ITenantProvisioningService _provisioning;
    private readonly ITransactionRunner _tx;
    private readonly IJwtTokenService _tokens;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _log;
    private readonly IEmailSender _email;
    private readonly TenantDatabaseProvisioner _databases;
    private readonly IOptionsMonitor<PlatformOptions> _platform;

    public AuthService(ErpDbContext db, ITenantContext tenant, ITenantProvisioningService provisioning, ITransactionRunner tx,
        IJwtTokenService tokens, IPasswordHasher<User> hasher, IConfiguration config, ILogger<AuthService> log, IEmailSender email,
        TenantDatabaseProvisioner databases, IOptionsMonitor<PlatformOptions> platform)
    {
        _db = db; _tenant = tenant; _provisioning = provisioning; _tx = tx;
        _tokens = tokens; _hasher = hasher; _config = config; _log = log; _email = email;
        _databases = databases; _platform = platform;
    }

    public async Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            throw new ValidationFailedException(Messages.EmailAndPasswordRequired);

        // الدخول يسبق معرفة المنشأة، لذلك يُتجاوز مرشّح المنشأة هنا فقط ويُستنتج المستأجر من المستخدم.
        // مقارنة مباشرة بلا ToLower على الأعمدة: ترتيب SQL Server غير حسّاس لحالة الأحرف، فيُستخدم فهرس البريد/الجوال بدل مسح الجدول.
        var candidates = request.Email.Trim().Contains('@')
            ? await FindByEmailAsync(request.Email.Trim(), ct)
            : await FindByPhoneAsync(request.Email.Trim(), ct);

        // تُفحص كل الحسابات المطابقة: إن صحّت كلمة المرور لأكثر من حساب (جوال مشترك بين منشآت بكلمة مرور واحدة)
        // لا يُختار أحدها، ويُطلب الدخول بالبريد الذي لا يتكرر بين المنشآت.
        var matches = candidates.Where(c => c.PasswordHash != null
            && _hasher.VerifyHashedPassword(c, c.PasswordHash, request.Password) != PasswordVerificationResult.Failed).Take(2).ToList();
        if (matches.Count == 0) throw new UnauthorizedAppException();
        if (matches.Count > 1) throw new UnauthorizedAppException(Messages.LoginAmbiguousUseEmail);
        var user = matches[0];

        _tenant.SetTenant(user.TenantId);
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is not { IsActive: true }) throw new ForbiddenException(Messages.CompanyInactive);

        // تحديث مباشر لعمود واحد: بلا تتبّع ولا مرور على سجل التدقيق (LastLoginAt ليس تعديلاً من مستخدم أصلاً).
        user.LastLoginAt = DateTime.UtcNow;
        var userId = user.Id;
        var loginAt = user.LastLoginAt;
        await _db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastLoginAt, loginAt), ct);

        return Build(user, tenant, Messages.LoginSucceeded);
    }

    /// <summary>البحث بالبريد عبر كل المنشآت: مقارنة مكشوفة على العمود تخدم فهرس Email مباشرة.</summary>
    private async Task<List<User>> FindByEmailAsync(string email, CancellationToken ct)
    {
        return await _db.Catalog.Set<User>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Email == email && u.IsActive)
            .OrderByDescending(u => u.LastLoginAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// البحث بالجوال عبر كل المنشآت: الرقم يُخزَّن كما أُدخل، فيُستعلام عن صيغه المحتملة بـ IN
    /// (عمود مكشوف بلا دوال تحويل ⇒ الاستعلام قابل للاستفادة من فهرس Phone).
    /// تعدد الحسابات بنفس الرقم بين المنشآت مسموح، وكلمة المرور هي ما يحسم الحساب في LoginAsync.
    /// </summary>
    private async Task<List<User>> FindByPhoneAsync(string input, CancellationToken ct)
    {
        var variants = PhoneCandidates(input);
        if (variants.Count == 0) return [];

        return await _db.Catalog.Set<User>().IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && variants.Contains(u.Phone))
            .OrderByDescending(u => u.LastLoginAt)
            .ToListAsync(ct);
    }

    /// <summary>صيغ رقم الجوال المحتملة في تخزين المشروع (كما أُدخل، بمفتاح الدولة السعودي 966، وبالشكل المحلي 05XXXXXXXX).</summary>
    public static List<string> PhoneCandidates(string input)
    {
        // الأرقام العربية-الهندية تُحوَّل إلى لاتينية، ويُزال البادئ الدولي 00.
        var digits = new string(input.Where(char.IsDigit).Select(ch => (char)('0' + (int)char.GetNumericValue(ch))).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];

        // إن بُدئ بمفتاح الدولة 966 تُشتق الشكل المحلي منه، وإلا يُسبق بالصفر إن غاب.
        var local = digits.StartsWith("966") ? "0" + digits[3..] : digits.StartsWith("0") ? digits : "0" + digits;
        var national = local.TrimStart('0');
        // رقم بلا خانات وطنية (مثل «0» أو «966») يطابق حسابات جوالها فارغ؛ لا يُبحث به.
        if (national.Length < 2) return [];
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            digits,            // كما أُدخلت
            local,             // 05XXXXXXXX
            national,          // 5XXXXXXXX
            "966" + national,  // 9665XXXXXXXX
            "+966" + national, // +9665XXXXXXXX
        };
        return set.ToList();
    }

    /// <summary>
    /// التسجيل العام: لا يحمل نوع قاعدة البيانات (الطلب نفسه لا يعرف الحقل)، وتُتبع القيمة التي اختارها مدير المنصة
    /// من لوحته (وإلا Platform:DefaultDatabaseMode، المشتركة افتراضياً). المستقلة تُجهَّز بعد تأكيد التسجيل في الدليل.
    /// </summary>
    public async Task<AuthResultDto> RegisterCompanyAsync(CompanyRegistrationRequestDto r, CancellationToken ct = default)
    {
        TenantOnboarding.Validate(r);
        var email = r.AdminEmail.Trim().ToLower();
        var tenant = TenantOnboarding.NewTenant(r, email);
        var dedicated = (await TenantOnboarding.DefaultDatabaseModeAsync(_db, _platform.CurrentValue, ct)).Mode == TenantDatabaseMode.Dedicated;
        // المنشأة الجديدة تُضبط قبل المعاملة: اتصالها يُفتح على القاعدة المشتركة (لا ربط لها بعد) مهما كان رمز المتصل
        _tenant.SetTenant(tenant.Id);

        var result = await _tx.RunAsync(async token =>
        {
            await TenantOnboarding.EnsureUniqueAsync(_db, r, email, token);
            _db.Add(tenant);

            // فترة تجريبية مجانية على الباقة المختارة؛ بعد انتهائها يُحجب النظام حتى سداد الاشتراك (POST /payments/subscription)
            var plan = await SubscriptionCatalog.GetAsync(_db, r.PlanId, forSale: true, token);
            _db.Add(SubscriptionCatalog.NewTrial(plan, r.BillingCycle));

            var admin = TenantOnboarding.NewOwner(r, email, _hasher);
            _db.Add(admin);
            if (dedicated) _db.Add(_databases.NewBinding(tenant.Id));

            await _db.SaveChangesAsync(token);
            if (!dedicated) await _provisioning.SeedAsync(tenant.Id, tenant.Currency, token);

            return Build(admin, tenant, string.Format(Messages.CompanyRegisteredWithTrial, tenant.NameAr));
        }, ct);

        if (dedicated) await _databases.EnsureReadyAsync(tenant.Id, ct);
        return result;
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
        if (id.Length == 0) throw new ValidationFailedException(Messages.EnterEmailOrMobile);

        var user = await _db.Catalog.Set<User>().IgnoreQueryFilters().AsNoTracking()
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

        var lastRequest = await _db.Set<PasswordResetOtp>().Where(o => o.UserId == user.Id)
            .OrderByDescending(o => o.CreatedAt).Select(o => (DateTime?)o.CreatedAt).FirstOrDefaultAsync(ct);
        if (lastRequest.HasValue && (DateTime.UtcNow - lastRequest.Value).TotalSeconds < OtpResendSeconds)
            throw new ValidationFailedException(Messages.OtpRequestTooSoon);

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

        var exposed = _config.GetValue<bool>("Auth:ExposeOtpInResponse");
        if (exposed) result.OtpCode = code; // للتطوير فقط

        if (_email.IsConfigured)
        {
            try
            {
                await _email.SendAsync(user.Email, PasswordResetEmail.Subject, PasswordResetEmail.Body(user.Name, code, OtpMinutes), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // الرمز لم يصل: لا نُظهر للمستخدم نجاحاً زائفاً ينتظر بعده رسالة لن تأتي
                _log.LogError(ex, "تعذّر إرسال رمز استعادة كلمة المرور للمستخدم {UserId}.", user.Id);
                if (!exposed) throw new ValidationFailedException(Messages.OtpEmailFailed);
            }
        }
        else if (!exposed)
            _log.LogWarning("لم يُضبط بريد الإرسال (Email)؛ رمز الاستعادة لم يُرسل للمستخدم {UserId}.", user.Id);

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
            throw new ValidationFailedException(string.Format(Messages.PasswordMinLength, MinPasswordLength));

        var otp = await FindValidOtpAsync(request.UserId, request.Otp, markAttempt: true, ct);
        var user = await _db.Set<User>().FirstAsync(u => u.Id == request.UserId, ct);

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword.Trim());
        otp.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new OperationResultDto { Message = "تم تغيير كلمة المرور بنجاح." };
    }

    // ---------------- رابط الدعوة (تفعيل حساب بوابة الموظف) ----------------
    public async Task<InviteInfoDto> GetInviteAsync(string token, CancellationToken ct = default)
    {
        var (user, _) = await FindInviteAsync(token, ct);
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(t => t.Id == user.TenantId, ct);
        return new InviteInfoDto { Name = user.Name, Email = user.Email, CompanyName = tenant.NameAr };
    }

    public async Task<AuthResultDto> AcceptInviteAsync(AcceptInviteRequestDto request, CancellationToken ct = default)
    {
        var password = request.Password?.Trim() ?? string.Empty;
        if (password.Length < MinPasswordLength)
            throw new ValidationFailedException(string.Format(Messages.PasswordMinLength, MinPasswordLength));

        var (user, invite) = await FindInviteAsync(request.Token, ct);
        var tenant = await _db.Set<Tenant>().FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        if (tenant is not { IsActive: true }) throw new ForbiddenException(Messages.CompanyInactive);

        user.PasswordHash = _hasher.HashPassword(user, password);
        user.LastLoginAt = DateTime.UtcNow;
        invite.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Build(user, tenant, Messages.LoginSucceeded);
    }

    /// <summary>الرابط يسبق معرفة المنشأة، فتُستنتج من الدعوة نفسها.</summary>
    private async Task<(User User, UserInvite Invite)> FindInviteAsync(string? token, CancellationToken ct)
    {
        var hash = InviteTokens.Hash(token?.Trim() ?? string.Empty);
        // الدعوة تُعرف من الدليل، ثم تُقرأ (وتُعدَّل) من قاعدة منشأتها
        var found = await _db.Catalog.Set<UserInvite>().IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TokenHash == hash && i.UsedAt == null).Select(i => new { i.Id, i.TenantId }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.InviteInvalidOrExpired);

        _tenant.SetTenant(found.TenantId);
        var invite = await _db.Set<UserInvite>().FirstOrDefaultAsync(i => i.Id == found.Id && i.UsedAt == null, ct);
        if (invite == null || invite.ExpiresAt < DateTime.UtcNow) throw new ValidationFailedException(Messages.InviteInvalidOrExpired);
        var user = await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == invite.UserId && u.IsActive, ct)
            ?? throw new ValidationFailedException(Messages.InviteInvalidOrExpired);
        return (user, invite);
    }

    private async Task<PasswordResetOtp> FindValidOtpAsync(Guid userId, string code, bool markAttempt, CancellationToken ct)
    {
        var user = await _db.Catalog.Set<User>().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw new ValidationFailedException(Messages.OtpInvalidOrExpired);
        _tenant.SetTenant(user.TenantId);

        var otp = await _db.Set<PasswordResetOtp>().Where(o => o.UserId == userId && o.UsedAt == null)
            .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync(ct);
        if (otp == null || otp.ExpiresAt < DateTime.UtcNow || otp.Attempts >= MaxOtpAttempts)
            throw new ValidationFailedException(Messages.OtpInvalidOrExpired);

        if (markAttempt) otp.Attempts++;
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(HashOtp(userId, code?.Trim() ?? string.Empty))))
        {
            await _db.SaveChangesAsync(ct);
            throw new ValidationFailedException(Messages.OtpIncorrect);
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
