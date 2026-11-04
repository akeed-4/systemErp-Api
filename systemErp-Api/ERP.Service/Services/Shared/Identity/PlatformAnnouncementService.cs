using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// إعلانات المنصة (في القاعدة المشتركة): يكتبها مدير المنصة ويحدد جمهورها (حالة الاشتراك، الباقات، منشآت بعينها)
/// وفترة ظهورها؛ والمنشأة تقرأ ما ينطبق عليها الآن فقط. إغلاق المستخدم للإعلان يُحفظ في متصفحه حسب «التكرار».
/// </summary>
public class PlatformAnnouncementService : IPlatformAnnouncementService
{
    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;

    public PlatformAnnouncementService(ErpDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    private ErpDbContext Catalog => _db.Catalog;

    // ---------- إدارة المنصة ----------

    public async Task<List<PlatformAnnouncementDto>> ListAsync(CancellationToken ct = default)
        => (await Catalog.Set<PlatformAnnouncement>().AsNoTracking()
                .OrderByDescending(a => a.Priority).ThenByDescending(a => a.CreatedAt).ToListAsync(ct))
            .Select(ToDto).ToList();

    public async Task<PlatformAnnouncementDto> CreateAsync(SavePlatformAnnouncementRequestDto r, CancellationToken ct = default)
    {
        var a = new PlatformAnnouncement();
        await ApplyAsync(a, r, ct);
        Catalog.Add(a);
        await Catalog.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<PlatformAnnouncementDto> UpdateAsync(Guid id, SavePlatformAnnouncementRequestDto r, CancellationToken ct = default)
    {
        var a = await Catalog.Set<PlatformAnnouncement>().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.AnnouncementNotFound);
        await ApplyAsync(a, r, ct);
        a.UpdatedAt = DateTime.UtcNow;
        await Catalog.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var a = await Catalog.Set<PlatformAnnouncement>().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.AnnouncementNotFound);
        Catalog.Remove(a);
        await Catalog.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(PlatformAnnouncement a, SavePlatformAnnouncementRequestDto r, CancellationToken ct)
    {
        var titleAr = Clean(r.TitleAr);
        var bodyAr = Clean(r.BodyAr);
        if (titleAr == null || bodyAr == null) throw new ValidationFailedException(Messages.AnnouncementTitleRequired);

        var style = Key(r.Style);
        var display = Key(r.Display);
        var audience = Key(r.Audience);
        var frequency = Key(r.Frequency);
        if (!AnnouncementStyles.All.Contains(style) || !AnnouncementDisplays.All.Contains(display)
            || !AnnouncementAudiences.Values.Contains(audience) || !AnnouncementFrequencies.All.Contains(frequency))
            throw new ValidationFailedException(Messages.AnnouncementOptionInvalid);

        var icon = Key(r.Icon);
        if (icon.Length == 0) icon = "campaign";
        if (icon.Length > 40 || !icon.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            throw new ValidationFailedException(Messages.AnnouncementOptionInvalid);

        var ctaUrl = Clean(r.CtaUrl);
        if (ctaUrl != null && !IsSafeLink(ctaUrl)) throw new ValidationFailedException(Messages.AnnouncementLinkInvalid);
        var ctaAr = Clean(r.CtaLabelAr);
        if (ctaUrl != null && ctaAr == null) throw new ValidationFailedException(Messages.AnnouncementLinkInvalid);

        if (r.StartsAt.HasValue && r.EndsAt.HasValue && r.EndsAt.Value <= r.StartsAt.Value)
            throw new ValidationFailedException(Messages.EndDateBeforeStart);

        var plans = (r.PlanIds ?? new()).Select(SubscriptionPlanId.Normalize).Where(x => x.Length > 0).Distinct().ToList();
        if (plans.Count > 0 && await Catalog.Set<PlanDefinition>().CountAsync(x => plans.Contains(x.Id), ct) != plans.Count)
            throw new ValidationFailedException(Messages.PromoCodePlanUnknown);
        var tenants = (r.TenantIds ?? new()).Where(x => x != Guid.Empty).Distinct().ToList();
        if (tenants.Count > 0 && await Catalog.Set<Tenant>().IgnoreQueryFilters().CountAsync(t => tenants.Contains(t.Id), ct) != tenants.Count)
            throw new ValidationFailedException(Messages.AnnouncementTenantUnknown);

        a.TitleAr = titleAr;
        a.TitleEn = Clean(r.TitleEn);
        a.BodyAr = bodyAr;
        a.BodyEn = Clean(r.BodyEn);
        a.BadgeAr = Clean(r.BadgeAr);
        a.BadgeEn = Clean(r.BadgeEn);
        a.CtaLabelAr = ctaAr;
        a.CtaLabelEn = Clean(r.CtaLabelEn);
        a.CtaUrl = ctaUrl;
        a.Icon = icon;
        a.Style = style;
        a.Display = display;
        a.Audience = audience;
        a.PlanIds = plans.Count == 0 ? null : string.Join(',', plans);
        a.TenantIds = tenants.Count == 0 ? null : string.Join(',', tenants);
        a.Frequency = frequency;
        a.Priority = Math.Clamp(r.Priority, 0, 1000);
        a.StartsAt = ToUtc(r.StartsAt);
        a.EndsAt = ToUtc(r.EndsAt);
        a.IsActive = r.IsActive;
    }

    // ---------- ما يظهر للمنشأة ----------

    public async Task<List<PlatformAnnouncementDto>> ActiveForCurrentTenantAsync(CancellationToken ct = default)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == null) return new();
        var now = DateTime.UtcNow;
        var candidates = await Catalog.Set<PlatformAnnouncement>().AsNoTracking()
            .Where(a => a.IsActive && (a.StartsAt == null || a.StartsAt <= now) && (a.EndsAt == null || a.EndsAt > now))
            .OrderByDescending(a => a.Priority).ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
        if (candidates.Count == 0) return new();

        var sub = await _db.Set<Subscription>().AsNoTracking().OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
        var tenantKey = tenantId.Value.ToString();
        return candidates.Where(a =>
            (a.Audience == AnnouncementAudiences.All
                || (a.Audience == AnnouncementAudiences.Trial && sub?.Status == SubscriptionStatus.Trial)
                || (a.Audience == AnnouncementAudiences.Paid && sub?.Status == SubscriptionStatus.Active))
            && (string.IsNullOrEmpty(a.PlanIds) || (sub != null && Split(a.PlanIds).Contains(SubscriptionPlanId.Normalize(sub.PlanType))))
            && (string.IsNullOrEmpty(a.TenantIds) || Split(a.TenantIds).Contains(tenantKey, StringComparer.OrdinalIgnoreCase)))
            // الاستهداف شأن المنصة: لا تُكشف للمنشأة معرّفات منشآت أخرى أو قائمة الباقات
            .Select(a => { var d = ToDto(a); d.PlanIds = new(); d.TenantIds = new(); d.Audience = AnnouncementAudiences.All; return d; })
            .ToList();
    }

    // ---------- مساعدات ----------

    /// <summary>مسار داخلي (/subscription) أو رابط https خارجي فقط؛ يمنع javascript: والروابط النسبية للبروتوكول (//).</summary>
    private static bool IsSafeLink(string url)
    {
        if (url.Length > 500) return false;
        if (url.StartsWith('/')) return !url.StartsWith("//") && !url.Contains('\\');
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Key(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
    private static string[] Split(string? csv) => (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static DateTime? ToUtc(DateTime? d) => d switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        // بلا منطقة زمنية: تُفهم بتوقيت المملكة كما يُدخلها المدير
        var v => DateTime.SpecifyKind(v.Value.AddHours(-3), DateTimeKind.Utc),
    };

    private static PlatformAnnouncementDto ToDto(PlatformAnnouncement a) => new()
    {
        Id = a.Id, TitleAr = a.TitleAr, TitleEn = a.TitleEn, BodyAr = a.BodyAr, BodyEn = a.BodyEn,
        BadgeAr = a.BadgeAr, BadgeEn = a.BadgeEn, CtaLabelAr = a.CtaLabelAr, CtaLabelEn = a.CtaLabelEn, CtaUrl = a.CtaUrl,
        Icon = a.Icon, Style = a.Style, Display = a.Display, Audience = a.Audience,
        PlanIds = Split(a.PlanIds).ToList(),
        TenantIds = Split(a.TenantIds).Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList(),
        Frequency = a.Frequency, Priority = a.Priority,
        StartsAt = a.StartsAt.HasValue ? DateTime.SpecifyKind(a.StartsAt.Value, DateTimeKind.Utc) : null,
        EndsAt = a.EndsAt.HasValue ? DateTime.SpecifyKind(a.EndsAt.Value, DateTimeKind.Utc) : null,
        IsActive = a.IsActive, CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt,
    };
}
