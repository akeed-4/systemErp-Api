using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

public class PlatformSubscriptionService : IPlatformSubscriptionService
{
    private const int ExpiringSoonDays = 14;
    private const int MaxExtendDays = 3650;

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher<User> _hasher;
    private readonly TenantDatabaseProvisioner _databases;
    private readonly IOptionsMonitor<PlatformOptions> _options;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public PlatformSubscriptionService(ErpDbContext db, ITenantContext tenant, IPasswordHasher<User> hasher, TenantDatabaseProvisioner databases,
        IOptionsMonitor<PlatformOptions> options, ICurrentUser user, IAuditService audit)
    {
        _db = db;
        _tenant = tenant;
        _hasher = hasher;
        _databases = databases;
        _options = options;
        _user = user;
        _audit = audit;
    }

    /// <summary>
    /// العرض عبر المنشآت والباقات والوحدات من القاعدة المشتركة (الدليل) أياً كانت قاعدة منشأة المدير؛
    /// وتعديل اشتراك منشأة يمرّ بسياقها الموجَّه (قاعدتها) ويُنسخ للدليل.
    /// </summary>
    private ErpDbContext Catalog => _db.Catalog;

    // ---------- العرض ----------

    public Task<List<PlatformModuleDto>> ListModulesAsync(CancellationToken ct = default)
        => Catalog.Set<PlatformModuleDefinition>().AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.SortOrder)
            .Select(m => new PlatformModuleDto
            {
                Number = m.Number, Key = m.Key, NameAr = m.NameAr, NameEn = m.NameEn,
                DescriptionAr = m.DescriptionAr, DescriptionEn = m.DescriptionEn,
                Icon = m.Icon, SortOrder = m.SortOrder, IsActive = m.IsActive,
            }).ToListAsync(ct);

    /// <summary>أحدث اشتراك لكل منشأة (بعد تجاوز مرشّح المنشأة).</summary>
    private async Task<Dictionary<Guid, Subscription>> LatestPerTenantAsync(CancellationToken ct)
    {
        var all = await Catalog.Set<Subscription>().IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        return all.GroupBy(s => s.TenantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.StartDate).ThenByDescending(s => s.CreatedAt).First());
    }

    /// <summary>الحالة الفعلية: الفعّال/التجريبي المنتهي تاريخه يُعدّ منتهياً.</summary>
    private static SubscriptionStatus Effective(Subscription s, DateTime now)
        => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial && s.ExpiryDate < now ? SubscriptionStatus.Expired : s.Status;

    public async Task<List<PlatformTenantSubscriptionDto>> ListTenantsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenants = await Catalog.Set<Tenant>().IgnoreQueryFilters().AsNoTracking().OrderBy(t => t.NameAr).ToListAsync(ct);
        var latest = await LatestPerTenantAsync(ct);
        var users = (await Catalog.Set<User>().IgnoreQueryFilters().AsNoTracking().Select(u => u.TenantId).ToListAsync(ct))
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var databases = await Catalog.Set<TenantDatabase>().AsNoTracking().ToDictionaryAsync(d => d.TenantId, ct);

        return tenants.Select(t =>
        {
            latest.TryGetValue(t.Id, out var sub);
            databases.TryGetValue(t.Id, out var database);
            return new PlatformTenantSubscriptionDto
            {
                TenantId = t.Id, NameAr = t.NameAr, NameEn = t.NameEn, VatNumber = t.VatNumber, Email = t.Email,
                Phone = t.Phone, City = t.City, TenantActive = t.IsActive, CreatedAt = t.CreatedAt,
                UsersCount = users.GetValueOrDefault(t.Id),
                Subscription = sub == null ? null : SubscriptionCatalog.ToDto(sub),
                EffectiveStatus = sub == null ? null : Effective(sub, now),
                DaysRemaining = sub == null ? null : (int)Math.Ceiling((sub.ExpiryDate - now).TotalDays),
                DatabaseMode = database?.Mode ?? TenantDatabaseMode.Shared,
                DatabaseStatus = database?.Status,
            };
        }).ToList();
    }

    // ---------- إعدادات المنصة ----------

    public async Task<PlatformSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var (mode, setting) = await TenantOnboarding.DefaultDatabaseModeAsync(_db, _options.CurrentValue, ct);
        return new PlatformSettingsDto
        {
            DefaultDatabaseMode = mode, IsCustomized = setting != null, UpdatedAt = setting?.UpdatedAt, UpdatedBy = setting?.UpdatedBy,
        };
    }

    public async Task<PlatformSettingsDto> UpdateSettingsAsync(UpdatePlatformSettingsDto request, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(request.DefaultDatabaseMode)) throw new ValidationFailedException(Messages.TenantDatabaseModeInvalid);
        var before = (await GetSettingsAsync(ct)).DefaultDatabaseMode;
        var setting = await Catalog.Set<PlatformSetting>().FirstOrDefaultAsync(s => s.Key == PlatformSetting.DefaultDatabaseMode, ct);
        if (setting == null) Catalog.Add(setting = new PlatformSetting { Key = PlatformSetting.DefaultDatabaseMode });
        setting.Value = request.DefaultDatabaseMode.ToString();
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedBy = _user.Email;
        await Catalog.SaveChangesAsync(ct);
        await _audit.LogAsync("PLATFORM_DEFAULT_DATABASE_MODE_CHANGED", nameof(PlatformSetting), PlatformSetting.DefaultDatabaseMode,
            $"{before} → {request.DefaultDatabaseMode}", ct);
        return await GetSettingsAsync(ct);
    }

    // ---------- إنشاء منشأة ونوع قاعدتها (لمدير المنصة وحده) ----------

    public async Task<PlatformTenantSubscriptionDto> CreateTenantAsync(PlatformCreateTenantRequestDto r, CancellationToken ct = default)
    {
        TenantOnboarding.Validate(r);
        if (!Enum.IsDefined(r.DatabaseMode) || !Enum.IsDefined(r.BillingCycle)) throw new ValidationFailedException(Messages.TenantDatabaseModeInvalid);
        var email = r.AdminEmail.Trim().ToLower();
        await TenantOnboarding.EnsureUniqueAsync(_db, r, email, ct);
        // مدير المنصة قد يمنح باقة معطّلة للبيع (كتغيير الباقة)
        var plan = await SubscriptionCatalog.GetAsync(_db, r.PlanId, forSale: false, ct);
        var dedicated = r.DatabaseMode == TenantDatabaseMode.Dedicated;

        var tenant = TenantOnboarding.NewTenant(r, email);
        _tenant.SetTenant(tenant.Id);
        // المنشأة ومالكها واشتراكها (وربط قاعدتها المستقلة) في الدليل بمعاملة واحدة؛ المشتركة تُزرع بياناتها الافتتاحية معها
        await using (var tx = await Catalog.Database.BeginTransactionAsync(ct))
        {
            Catalog.Add(tenant);
            var sub = SubscriptionCatalog.NewSubscription(plan, r.BillingCycle, "manual");
            sub.TransactionReference = $"ADM-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            Catalog.Add(sub);
            Catalog.Add(TenantOnboarding.NewOwner(r, email, _hasher));
            if (dedicated) Catalog.Add(_databases.NewBinding(tenant.Id));
            await Catalog.SaveChangesAsync(ct);
            if (!dedicated) await new TenantProvisioningService(Catalog).SeedAsync(tenant.Id, tenant.Currency, ct);
            await tx.CommitAsync(ct);
        }

        if (dedicated) await _databases.EnsureReadyAsync(tenant.Id, ct);
        return await TenantRowAsync(tenant.Id, ct);
    }

    public async Task<PlatformTenantSubscriptionDto> ProvisionDatabaseAsync(Guid tenantId, CancellationToken ct = default)
    {
        await EnsureTenantAsync(tenantId, ct);
        await _databases.EnsureReadyAsync(tenantId, ct); // المنشأة المشتركة تُرفض: نوع القاعدة لا يتغيّر بعد الإنشاء
        return await TenantRowAsync(tenantId, ct);
    }

    private async Task<PlatformTenantSubscriptionDto> TenantRowAsync(Guid tenantId, CancellationToken ct)
        => (await ListTenantsAsync(ct)).First(t => t.TenantId == tenantId);

    public async Task<PlatformSummaryDto> SummaryAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantIds = await Catalog.Set<Tenant>().IgnoreQueryFilters().AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        var latest = await LatestPerTenantAsync(ct);
        var r = new PlatformSummaryDto { TotalTenants = tenantIds.Count };
        foreach (var id in tenantIds)
        {
            if (!latest.TryGetValue(id, out var s)) { r.NoSubscription++; continue; }
            switch (Effective(s, now))
            {
                case SubscriptionStatus.Active:
                    r.Active++;
                    r.MonthlyRecurringRevenue += s.BillingCycle == SubscriptionBillingCycle.Yearly ? Math.Round(s.Price / 12, 2) : s.Price;
                    if ((s.ExpiryDate - now).TotalDays <= ExpiringSoonDays) r.ExpiringSoon++;
                    break;
                case SubscriptionStatus.Trial: r.Trial++; break;
                case SubscriptionStatus.Expired: r.Expired++; break;
                case SubscriptionStatus.Suspended: r.Suspended++; break;
                case SubscriptionStatus.Cancelled: r.Cancelled++; break;
            }
        }
        return r;
    }

    public async Task<List<SubscriptionDto>> HistoryAsync(Guid tenantId, CancellationToken ct = default)
    {
        await EnsureTenantAsync(tenantId, ct);
        var subs = await Catalog.Set<Subscription>().IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartDate).ThenByDescending(s => s.CreatedAt).ToListAsync(ct);
        return subs.Select(s => SubscriptionCatalog.ToDto(s)).ToList();
    }

    // ---------- الإجراءات على اشتراك منشأة ----------

    private async Task EnsureTenantAsync(Guid tenantId, CancellationToken ct)
    {
        if (!await Catalog.Set<Tenant>().IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId, ct))
            throw new NotFoundException("المنشأة غير موجودة.");
    }

    /// <summary>يحوّل سياق الطلب إلى المنشأة المستهدفة (مرشّح المنشأة وختم TenantId يتبعانه) بعد التحقق من وجودها.</summary>
    private async Task ScopeToTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await EnsureTenantAsync(tenantId, ct);
        _tenant.SetTenant(tenantId);
    }

    private async Task<Subscription> LatestAsync(CancellationToken ct)
        => await _db.Set<Subscription>().OrderByDescending(s => s.StartDate).ThenByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException("لا يوجد اشتراك لهذه المنشأة.");

    public async Task<SubscriptionDto> ChangePlanAsync(Guid tenantId, PlatformChangePlanRequestDto request, CancellationToken ct = default)
    {
        if (!SubscriptionPlanId.IsValid(request.PlanId) || !Enum.IsDefined(request.BillingCycle))
            throw new ValidationFailedException("الباقة أو دورة الفوترة غير صالحة.");
        await ScopeToTenantAsync(tenantId, ct);
        // مدير المنصة قد يمنح باقة معطّلة للبيع.
        var plan = await SubscriptionCatalog.GetAsync(_db, request.PlanId, forSale: false, ct);

        var open = await _db.Set<Subscription>()
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trial || s.Status == SubscriptionStatus.Suspended)
            .ToListAsync(ct);
        foreach (var s in open) s.Status = SubscriptionStatus.Expired;

        var method = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "manual" : request.PaymentMethod.Trim();
        var sub = SubscriptionCatalog.NewSubscription(plan, request.BillingCycle, method, request.Modules);
        sub.TransactionReference = $"ADM-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        _db.Add(sub);
        await _db.SaveChangesAsync(ct);
        return SubscriptionCatalog.ToDto(sub);
    }

    public async Task<SubscriptionDto> ExtendAsync(Guid tenantId, PlatformExtendRequestDto request, CancellationToken ct = default)
    {
        if (request.Days < 1 || request.Days > MaxExtendDays)
            throw new ValidationFailedException($"عدد الأيام يجب أن يكون بين 1 و{MaxExtendDays}.");
        await ScopeToTenantAsync(tenantId, ct);
        var sub = await LatestAsync(ct);
        if (sub.Status == SubscriptionStatus.Cancelled)
            throw new ValidationFailedException("الاشتراك ملغى؛ فعّله أولاً أو غيّر الباقة.");

        var now = DateTime.UtcNow;
        var start = sub.ExpiryDate > now ? sub.ExpiryDate : now; // المنتهي يُمدَّد من اليوم
        sub.ExpiryDate = start.AddDays(request.Days);
        if (sub.Status == SubscriptionStatus.Expired) sub.Status = SubscriptionStatus.Active;
        await _db.SaveChangesAsync(ct);
        return SubscriptionCatalog.ToDto(sub);
    }

    public async Task<SubscriptionDto> SetStatusAsync(Guid tenantId, PlatformSetStatusRequestDto request, CancellationToken ct = default)
    {
        if (request.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled))
            throw new ValidationFailedException("الحالة المسموحة: تفعيل أو إيقاف أو إلغاء.");
        await ScopeToTenantAsync(tenantId, ct);
        var sub = await LatestAsync(ct);
        if (request.Status == SubscriptionStatus.Active && sub.ExpiryDate < DateTime.UtcNow)
            throw new ValidationFailedException("انتهى تاريخ الاشتراك؛ مدّده أولاً ثم فعّله.");
        sub.Status = request.Status;
        await _db.SaveChangesAsync(ct);
        return SubscriptionCatalog.ToDto(sub);
    }

    public async Task<SubscriptionDto> SetModulesAsync(Guid tenantId, PlatformSetModulesRequestDto request, CancellationToken ct = default)
    {
        var modules = PlatformModules.Normalize(request.Modules);
        await ScopeToTenantAsync(tenantId, ct);
        var sub = await LatestAsync(ct);
        sub.ModuleKeys = PlatformModules.ToCsv(modules);
        await _db.SaveChangesAsync(ct);
        return SubscriptionCatalog.ToDto(sub);
    }

    // ---------- الباقات ----------

    private static PlatformPlanDto ToPlanDto(PlanDefinition p, int subscribers) => new()
    {
        Id = p.Id, NameAr = p.NameAr, NameEn = p.NameEn, PriceMonthly = p.PriceMonthly, PriceYearly = p.PriceYearly,
        MaxUsers = p.MaxUsers, MaxInvoicesPerMonth = p.MaxInvoicesPerMonth, Branches = p.Branches,
        ZatcaPhase2Enabled = p.ZatcaPhase2Enabled, Modules = PlatformModules.Parse(p.ModuleKeys),
        IsActive = p.IsActive, SortOrder = p.SortOrder, Subscribers = subscribers,
        DescriptionAr = p.DescriptionAr, DescriptionEn = p.DescriptionEn, BadgeAr = p.BadgeAr, BadgeEn = p.BadgeEn, IsPopular = p.IsPopular,
        FeaturesAr = SubscriptionCatalog.SplitLines(p.FeaturesAr), FeaturesEn = SubscriptionCatalog.SplitLines(p.FeaturesEn),
    };

    private async Task<Dictionary<string, int>> SubscriberCountsAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return (await LatestPerTenantAsync(ct)).Values
            .Where(s => Effective(s, now) is SubscriptionStatus.Active or SubscriptionStatus.Trial)
            .GroupBy(s => s.PlanType).ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<List<PlatformPlanDto>> ListPlansAsync(CancellationToken ct = default)
    {
        var counts = await SubscriberCountsAsync(ct);
        return (await Catalog.Set<PlanDefinition>().AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync(ct))
            .Select(p => ToPlanDto(p, counts.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<PlatformPlanDto> CreatePlanAsync(CreatePlanRequestDto r, CancellationToken ct = default)
    {
        var id = SubscriptionPlanId.Normalize(r.Id);
        if (!SubscriptionPlanId.IsValid(id))
            throw new ValidationFailedException("رمز الباقة مطلوب: من 2 إلى 64 حرفاً إنجليزياً صغيراً أو رقماً أو - أو _ (مثل gold أو pos-plus).");
        if (await Catalog.Set<PlanDefinition>().AnyAsync(p => p.Id == id, ct))
            throw new ConflictException($"يوجد باقة بالرمز «{id}».");

        var plan = new PlanDefinition
        {
            Id = id,
            SortOrder = r.SortOrder ?? (await Catalog.Set<PlanDefinition>().MaxAsync(p => (int?)p.SortOrder, ct) ?? 0) + 1,
        };
        await ApplyAsync(plan, r, isNew: true, ct);
        Catalog.Add(plan);
        await Catalog.SaveChangesAsync(ct);
        return ToPlanDto(plan, 0);
    }

    public async Task<PlatformPlanDto> UpdatePlanAsync(string id, UpdatePlanRequestDto r, CancellationToken ct = default)
    {
        id = SubscriptionPlanId.Normalize(id);
        var plan = await Catalog.Set<PlanDefinition>().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("الباقة غير موجودة.");
        await ApplyAsync(plan, r, isNew: false, ct);
        await Catalog.SaveChangesAsync(ct);
        return ToPlanDto(plan, (await SubscriberCountsAsync(ct)).GetValueOrDefault(id));
    }

    /// <summary>
    /// حذف باقة لم تُستخدم: لا اشتراك عليها (حالي أو سابق، في أي منشأة) ولا عملية دفع، وليست آخر باقة فعّالة.
    /// الباقة المستخدمة تبقى لسجلات مشتركيها وتُعطَّل بدل الحذف.
    /// </summary>
    public async Task DeletePlanAsync(string id, CancellationToken ct = default)
    {
        id = SubscriptionPlanId.Normalize(id);
        var plan = await Catalog.Set<PlanDefinition>().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("الباقة غير موجودة.");
        if (await Catalog.Set<Subscription>().IgnoreQueryFilters().AnyAsync(s => s.PlanType == id, ct)
            || await Catalog.Set<OnlinePayment>().IgnoreQueryFilters().AnyAsync(p => p.PlanId == id, ct))
            throw new ConflictException("الباقة مستخدمة في اشتراكات أو مدفوعات فلا تُحذف؛ عطّلها لإيقاف بيعها.");
        if (plan.IsActive && !await Catalog.Set<PlanDefinition>().AnyAsync(p => p.IsActive && p.Id != id, ct))
            throw new ValidationFailedException("لا يمكن حذف آخر باقة فعّالة.");
        Catalog.Remove(plan);
        await Catalog.SaveChangesAsync(ct);
    }

    /// <summary>تحقق حقول الباقة وتطبيقها (الإنشاء والتعديل). في التعديل لا تُمسح التفاصيل التسويقية غير المرسلة.</summary>
    private async Task ApplyAsync(PlanDefinition plan, UpdatePlanRequestDto r, bool isNew, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.NameAr) || string.IsNullOrWhiteSpace(r.NameEn))
            throw new ValidationFailedException("اسم الباقة بالعربية والإنجليزية مطلوب.");
        if (r.PriceMonthly < 0 || r.PriceYearly < 0) throw new ValidationFailedException("السعر لا يمكن أن يكون سالباً.");
        if (r.MaxUsers is <= 0 || r.MaxInvoicesPerMonth is <= 0 || r.Branches is <= 0)
            throw new ValidationFailedException("حدود الباقة يجب أن تكون أكبر من صفر (اتركها فارغة لغير المحدود).");
        if (!isNew && !r.IsActive && plan.IsActive && !await Catalog.Set<PlanDefinition>().AnyAsync(p => p.IsActive && p.Id != plan.Id, ct))
            throw new ValidationFailedException("لا يمكن تعطيل آخر باقة فعّالة.");

        plan.NameAr = r.NameAr.Trim();
        plan.NameEn = r.NameEn.Trim();
        plan.PriceMonthly = r.PriceMonthly;
        plan.PriceYearly = r.PriceYearly;
        plan.MaxUsers = r.MaxUsers;
        plan.MaxInvoicesPerMonth = r.MaxInvoicesPerMonth;
        plan.Branches = r.Branches;
        plan.ZatcaPhase2Enabled = r.ZatcaPhase2Enabled;
        plan.ModuleKeys = PlatformModules.ToCsv(PlatformModules.Normalize(r.Modules));
        plan.IsActive = r.IsActive;
        if (r.DescriptionAr != null) plan.DescriptionAr = r.DescriptionAr.Trim();
        if (r.DescriptionEn != null) plan.DescriptionEn = r.DescriptionEn.Trim();
        if (r.BadgeAr != null) plan.BadgeAr = r.BadgeAr.Trim();
        if (r.BadgeEn != null) plan.BadgeEn = r.BadgeEn.Trim();
        if (r.FeaturesAr != null) plan.FeaturesAr = SubscriptionCatalog.JoinLines(r.FeaturesAr);
        if (r.FeaturesEn != null) plan.FeaturesEn = SubscriptionCatalog.JoinLines(r.FeaturesEn);
        if (r.IsPopular.HasValue)
        {
            // باقة مُبرَزة واحدة على الأكثر
            if (r.IsPopular.Value)
                foreach (var other in await Catalog.Set<PlanDefinition>().Where(p => p.IsPopular && p.Id != plan.Id).ToListAsync(ct))
                    other.IsPopular = false;
            plan.IsPopular = r.IsPopular.Value;
        }
        plan.UpdatedAt = DateTime.UtcNow;
    }
}
