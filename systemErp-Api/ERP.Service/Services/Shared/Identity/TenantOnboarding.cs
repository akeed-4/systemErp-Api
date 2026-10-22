using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.AspNetCore.Identity;

namespace ERP.Service.Services.Shared;

/// <summary>بناء منشأة جديدة ومالكها: مشترك بين التسجيل العام وإنشاء مدير المنصة، فتبقى قواعد التحقق والتفرّد واحدة.</summary>
internal static class TenantOnboarding
{
    public const int MinPasswordLength = 8;

    public static void Validate(CompanyRegistrationRequestDto r)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.CompanyNameAr)) errors.Add(Messages.CompanyArabicNameRequired);
        if (!SaudiVat.IsValid(r.VatNumber))
            errors.Add(Messages.SaudiVatNumberFormat);
        if (string.IsNullOrWhiteSpace(r.AdminName)) errors.Add(Messages.AdminNameRequired);
        if (string.IsNullOrWhiteSpace(r.AdminEmail) || !r.AdminEmail.Contains('@')) errors.Add(Messages.AdminEmailInvalid);
        if (r.Password is null || r.Password.Length < MinPasswordLength) errors.Add(string.Format(Messages.PasswordMinLength, MinPasswordLength));
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>الرقم الضريبي والبريد فريدان عبر كل المنشآت (من الدليل في القاعدة المشتركة).</summary>
    public static async Task EnsureUniqueAsync(ErpDbContext db, CompanyRegistrationRequestDto r, string email, CancellationToken ct)
    {
        if (await db.Catalog.Set<Tenant>().IgnoreQueryFilters().AnyAsync(t => t.VatNumber == r.VatNumber, ct))
            throw new ConflictException(Messages.VatNumberAlreadyRegistered);
        if (await db.Catalog.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == email, ct))
            throw new ConflictException(Messages.EmailAlreadyRegistered);
    }

    /// <summary>
    /// قاعدة المنشأة من التسجيل العام: ما اختاره مدير المنصة من لوحته، وإلا المضبوط في إعدادات الخادم (المشتركة افتراضياً).
    /// يُقرأ عند كل تسجيل، فتغييره يسري على التسجيل التالي فوراً ولا يمسّ منشأة قائمة.
    /// </summary>
    public static async Task<(TenantDatabaseMode Mode, PlatformSetting? Setting)> DefaultDatabaseModeAsync(ErpDbContext db, PlatformOptions options, CancellationToken ct)
    {
        var setting = await db.Catalog.Set<PlatformSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.Key == PlatformSetting.DefaultDatabaseMode, ct);
        return setting != null && Enum.TryParse<TenantDatabaseMode>(setting.Value, out var mode) && Enum.IsDefined(mode)
            ? (mode, setting)
            : (options.DefaultDatabaseMode, null);
    }

    public static Tenant NewTenant(CompanyRegistrationRequestDto r, string email)
    {
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
        return tenant;
    }

    public static User NewOwner(CompanyRegistrationRequestDto r, string email, IPasswordHasher<User> hasher)
    {
        var owner = new User
        {
            Name = r.AdminName.Trim(),
            Email = email,
            Phone = r.AdminPhone,
            Role = UserRole.Owner,
            AvatarInitials = AuthService.Initials(r.AdminName),
            LastLoginAt = DateTime.UtcNow,
        };
        owner.PasswordHash = hasher.HashPassword(owner, r.Password);
        return owner;
    }
}
