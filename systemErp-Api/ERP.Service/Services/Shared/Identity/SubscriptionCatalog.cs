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
                ZatcaPhase2Enabled = false, IsActive = true, SortOrder = 1,
                DescriptionAr = "مثالية للمنشآت والمتاجر الناشئة ورواد الأعمال لتلبية متطلبات الفوترة الضريبية وإدارة المعاملات الأساسية.",
                DescriptionEn = "Ideal for startups, sole proprietors, and small shops to meet basic invoicing and tax compliance.",
                BadgeAr = "للمنشآت الناشئة", BadgeEn = "For Startups", IsPopular = false,
                FeaturesAr = "مستخدمين 2 مع صلاحيات أساسية\nحتى 500 فاتورة مبيعات ومشتريات شهرياً\nفرع ومستودع رئيسي واحد\nإصدار فواتير ضريبية مبسطة (B2C) مع QR كود TLV\nسندات القبض والصرف الأساسية\nشجرة حسابات مبسطة (4 مستويات)\nدعم فني عبر البريد وتحديثات دورية",
                FeaturesEn = "Up to 2 users with basic roles\nUp to 500 invoices/month\n1 branch & single warehouse\nSimplified Tax Invoices (B2C) with TLV QR Code\nBasic receipt & payment vouchers\nStandard chart of accounts (4 levels)\nEmail support & regular updates" },
        new() { Id = SubscriptionPlanId.Professional, NameAr = "باقة الشركات المتقدمة (Professional)", NameEn = "Professional Business Plan",
                PriceMonthly = 499, PriceYearly = 4990, MaxUsers = 10, MaxInvoicesPerMonth = null, Branches = 3,
                ZatcaPhase2Enabled = false, IsActive = true, SortOrder = 2,
                DescriptionAr = "الخيار الأكثر طلباً للشركات والمؤسسات المتوسطة مع فواتير ضريبية برمز QR (المرحلة الأولى من ZATCA) ومحاسبة التكاليف.",
                DescriptionEn = "Most popular choice for growing businesses with QR tax invoices (ZATCA Phase 1) and a costing engine.",
                BadgeAr = "الأكثر طلباً ⭐", BadgeEn = "Most Popular ⭐", IsPopular = true,
                FeaturesAr = "حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)\nفواتير مبيعات ومشتريات غير محدودة شهرياً\nإدارة حتى 3 فروع ومستودعات متعددة\nفواتير ضريبية (B2B) ومبسطة (B2C) برمز QR وفق المرحلة الأولى من ZATCA\nمحرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)\nشجرة حسابات احترافية مرنة والقيود اليومية الآلية\nتقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)\nدعم فني سريع عبر الواتساب والهاتف",
                FeaturesEn = "Up to 10 users with advanced RBAC permissions\nUnlimited invoices per month\nUp to 3 branches & multi-warehouse inventory\nTax (B2B) and simplified (B2C) invoices with QR code (ZATCA Phase 1)\nWeighted Moving Average costing engine\nEnterprise chart of accounts with auto journal entries\nFinancial reports (P&L, Balance Sheet, VAT Return)\nPriority phone & WhatsApp support" },
        new() { Id = SubscriptionPlanId.Enterprise, NameAr = "باقة المجموعات والمؤسسات (Enterprise)", NameEn = "Enterprise Corporate Plan",
                PriceMonthly = 999, PriceYearly = 9990, MaxUsers = null, MaxInvoicesPerMonth = null, Branches = null,
                ZatcaPhase2Enabled = false, IsActive = true, SortOrder = 3,
                DescriptionAr = "حل متكامل ومخصص للمجموعات التجارية والشركات الكبرى وفروعها مع بنية تحتية مخصصة وربط API مفتوح.",
                DescriptionEn = "Custom corporate solution for large holdings and chains with dedicated infrastructure and API webhooks.",
                BadgeAr = "للمجموعات الكبرى", BadgeEn = "Corporate & Holding", IsPopular = false,
                FeaturesAr = "عدد غير محدود من المستخدمين والمشرفين\nفروع ومستودعات ومراكز تكلفة غير محدودة\nربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع\nفواتير ضريبية برمز QR وفق المرحلة الأولى من ZATCA\nتعدد العملات وسعر الصرف التلقائي\nتقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص\nخادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري\nمدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7",
                FeaturesEn = "Unlimited users and branch supervisors\nUnlimited branches, warehouses & cost centers\nFull RESTful API & POS webhooks integration\nQR tax invoices compliant with ZATCA Phase 1\nMulti-currency support with auto FX rates\nAdvanced financial BI dashboards & custom export\nDedicated high-performance tenant database & real-time backup\nDedicated account manager & 24/7 priority SLA" },
    };

    public static SubscriptionPlanDto ToDto(PlanDefinition p) => new()
    {
        Id = p.Id, NameAr = p.NameAr, NameEn = p.NameEn, PriceMonthly = p.PriceMonthly, PriceYearly = p.PriceYearly,
        MaxUsers = p.MaxUsers, MaxInvoicesPerMonth = p.MaxInvoicesPerMonth, Branches = p.Branches,
        ZatcaPhase2Enabled = p.ZatcaPhase2Enabled, Modules = PlatformModules.Parse(p.ModuleKeys),
        DescriptionAr = p.DescriptionAr, DescriptionEn = p.DescriptionEn, BadgeAr = p.BadgeAr, BadgeEn = p.BadgeEn, IsPopular = p.IsPopular,
        FeaturesAr = SplitLines(p.FeaturesAr), FeaturesEn = SplitLines(p.FeaturesEn),
    };

    /// <summary>المزايا تُخزَّن ميزة في كل سطر.</summary>
    public static string[] SplitLines(string? text)
        => (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string JoinLines(IEnumerable<string>? lines)
        => string.Join('\n', (lines ?? Array.Empty<string>()).Select(l => l?.Trim() ?? string.Empty).Where(l => l.Length > 0));

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
        var remaining = s.ExpiryDate - DateTime.UtcNow;
        dto.DaysRemaining = (int)Math.Ceiling(remaining.TotalDays);
        dto.TrialEnded = s.Status == SubscriptionStatus.Trial && remaining < TimeSpan.Zero;
        return dto;
    }

    /// <summary>مدة الفترة التجريبية المجانية للمنشأة الجديدة.</summary>
    public const int TrialMonths = 1;

    /// <summary>
    /// الفترة التجريبية المجانية لمنشأة جديدة: شهر كامل على الباقة المختارة بلا دفع.
    /// السعر المحفوظ هو المستحق عند انتهائها؛ بعدها يُحجب النظام حتى السداد.
    /// </summary>
    public static Subscription NewTrial(SubscriptionPlanDto plan, SubscriptionBillingCycle cycle)
    {
        var trial = NewSubscription(plan, cycle, "trial");
        trial.Status = SubscriptionStatus.Trial;
        trial.ExpiryDate = trial.StartDate.AddMonths(TrialMonths);
        trial.AutoRenew = false;
        return trial;
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
            ModuleKeys = PlatformModules.ToCsv(modules != null ? PlatformModules.Normalize(modules) : plan.Modules.Length > 0 ? plan.Modules : PlatformModules.Parse(PlatformModules.DefaultCsv)),
        };
    }
}
