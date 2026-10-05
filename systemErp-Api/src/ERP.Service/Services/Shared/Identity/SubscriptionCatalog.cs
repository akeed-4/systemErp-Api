using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// كتالوج الباقات. القيم الافتراضية (starter / professional / enterprise) تُزرع في جدول الباقات،
/// والمرجع وقت البيع هو قاعدة البيانات لأن مدير المنصة يعدّل الأسعار والحدود.
/// </summary>
public static class SubscriptionCatalog
{
    public const decimal VatRate = 0.15m;

    /// <summary>البيانات الأولية للباقات (تُزرع بالـ migration).</summary>
    public static readonly List<PlanDefinition> Seed = new()
    {
        new() { Id = SubscriptionPlanId.Starter, NameAr = "باقة البداية (Starter)", NameEn = "Starter Plan",
                PriceMonthly = 199, PriceYearly = 1990, MaxUsers = 2, MaxInvoicesPerMonth = 500, Branches = 1,
                ZatcaPhase2Enabled = false, IsActive = true, SortOrder = 1 },
        new() { Id = SubscriptionPlanId.Professional, NameAr = "باقة الشركات المتقدمة (Professional)", NameEn = "Professional Business Plan",
                PriceMonthly = 499, PriceYearly = 4990, MaxUsers = 10, MaxInvoicesPerMonth = null, Branches = 3,
                ZatcaPhase2Enabled = true, IsActive = true, SortOrder = 2 },
        new() { Id = SubscriptionPlanId.Enterprise, NameAr = "باقة المجموعات والمؤسسات (Enterprise)", NameEn = "Enterprise Corporate Plan",
                PriceMonthly = 999, PriceYearly = 9990, MaxUsers = null, MaxInvoicesPerMonth = null, Branches = null,
                ZatcaPhase2Enabled = true, IsActive = true, SortOrder = 3 },
    };

    public static SubscriptionPlanDto ToDto(PlanDefinition p) => new()
    {
        Id = p.Id, NameAr = p.NameAr, NameEn = p.NameEn, PriceMonthly = p.PriceMonthly, PriceYearly = p.PriceYearly,
        MaxUsers = p.MaxUsers, MaxInvoicesPerMonth = p.MaxInvoicesPerMonth, Branches = p.Branches,
        ZatcaPhase2Enabled = p.ZatcaPhase2Enabled, Modules = PlatformModules.Parse(p.ModuleKeys),
    };

    /// <summary>الباقة من قاعدة البيانات. <paramref name="forSale"/>: يرفض الباقة المعطّلة (تسجيل/دفع/ترقية ذاتية).</summary>
    public static async Task<SubscriptionPlanDto> GetAsync(ErpDbContext db, SubscriptionPlanId id, bool forSale, CancellationToken ct = default)
    {
        var plan = await db.Set<PlanDefinition>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new ValidationFailedException("الباقة غير موجودة.");
        if (forSale && !plan.IsActive) throw new ValidationFailedException("هذه الباقة غير متاحة حالياً.");
        return ToDto(plan);
    }

    public static SubscriptionDto ToDto(Subscription s)
    {
        var dto = Mapper.Map<SubscriptionDto>(s);
        dto.Modules = PlatformModules.Parse(s.ModuleKeys);
        return dto;
    }

    public static Subscription NewSubscription(SubscriptionPlanDto plan, SubscriptionBillingCycle cycle, string paymentMethod, IEnumerable<string>? modules = null)
    {
        var price = cycle == SubscriptionBillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;
        var vat = Math.Round(price * VatRate, 2);
        var now = DateTime.UtcNow;
        return new Subscription
        {
            PlanType = plan.Id,
            PlanNameAr = plan.NameAr,
            PlanNameEn = plan.NameEn,
            BillingCycle = cycle,
            Price = price,
            VatAmount = vat,
            TotalAmount = price + vat,
            StartDate = now,
            ExpiryDate = cycle == SubscriptionBillingCycle.Yearly ? now.AddYears(1) : now.AddMonths(1),
            Status = SubscriptionStatus.Active,
            PaymentMethod = paymentMethod,
            TransactionReference = $"TXN-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            MaxUsers = plan.MaxUsers ?? int.MaxValue,
            MaxBranches = plan.Branches ?? int.MaxValue,
            ZatcaPhase2Enabled = plan.ZatcaPhase2Enabled,
            ModuleKeys = PlatformModules.ToCsv(modules != null ? PlatformModules.Normalize(modules) : plan.Modules.Length > 0 ? plan.Modules : PlatformModules.All),
        };
    }
}
