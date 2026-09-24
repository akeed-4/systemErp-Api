using Microsoft.EntityFrameworkCore;

namespace Erp.Catalog.Persistence;

/// <summary>Master reference data (from the frontend's SYSTEM_SCREENS, roles and subscription plans). Upserted by the migrator.</summary>
internal static class CatalogSeedData
{
    public static readonly CatalogRole[] Roles =
    [
        new() { Code = "owner", NameAr = "مالك المنشأة", NameEn = "Owner", SortOrder = 1 },
        new() { Code = "admin", NameAr = "مدير النظام", NameEn = "Administrator", SortOrder = 2 },
        new() { Code = "general_manager", NameAr = "المدير العام", NameEn = "General Manager", SortOrder = 3 },
        new() { Code = "chief_accountant", NameAr = "رئيس الحسابات", NameEn = "Chief Accountant", SortOrder = 4 },
        new() { Code = "sales_rep", NameAr = "مندوب المبيعات", NameEn = "Sales Representative", SortOrder = 5 },
    ];

    public static readonly CatalogScreen[] Screens =
    [
        new() { Id = "dashboard", NameAr = "لوحة المؤشرات والملخص المالي", NameEn = "Dashboard & Summary", SortOrder = 1 },
        new() { Id = "master-data", NameAr = "البيانات الرئيسية (العملاء/الموردين/الأصناف)", NameEn = "Master Data & Entities", SortOrder = 2 },
        new() { Id = "sales", NameAr = "فواتير المبيعات ونقاط البيع (POS)", NameEn = "Sales Invoices & POS", SortOrder = 3 },
        new() { Id = "sales-returns", NameAr = "مرتجع المبيعات والإشعارات الدائنة", NameEn = "Sales Returns & Credit Notes", SortOrder = 4 },
        new() { Id = "purchases", NameAr = "فواتير المشتريات وتكلفة المخزون", NameEn = "Purchase Invoices", SortOrder = 5 },
        new() { Id = "vouchers", NameAr = "سندات القبض والصرف", NameEn = "Receipt & Payment Vouchers", SortOrder = 6 },
        new() { Id = "accounts", NameAr = "دليل الحسابات والقيود اليومية", NameEn = "Chart of Accounts", SortOrder = 7 },
        new() { Id = "zatca", NameAr = "الربط الإلكتروني والفوترة (ZATCA)", NameEn = "ZATCA Integration", SortOrder = 8 },
        new() { Id = "car-showroom", NameAr = "إدارة وتعاريف معارض السيارات والمبايعات", NameEn = "Car Showroom & Automotive", SortOrder = 9 },
        new() { Id = "reports", NameAr = "التقارير المالية والإقرار الضريبي", NameEn = "Financial Reports", SortOrder = 10 },
        new() { Id = "approval-policies", NameAr = "سياسات الموافقات والطلبات", NameEn = "Approval Policies & Requests", SortOrder = 11 },
        new() { Id = "user-permissions", NameAr = "إدارة صلاحيات المستخدمين والشاشات", NameEn = "User Roles & Screen Permissions", SortOrder = 12 },
    ];

    public static SubscriptionPlan[] Plans() =>
    [
        Plan(
            "starter", 1, "باقة البداية (Starter)", "Starter Plan",
            "مثالية للمنشآت والمتاجر الناشئة ورواد الأعمال لتلبية متطلبات الفوترة الضريبية وإدارة المعاملات الأساسية.",
            "Ideal for startups, sole proprietors, and small shops to meet basic invoicing and tax compliance.",
            199, 1990, false, "للمنشآت الناشئة", "For Startups", 2, 500, 1,
            [
                ("مستخدمين 2 مع صلاحيات أساسية", "Up to 2 users with basic roles"),
                ("حتى 500 فاتورة مبيعات ومشتريات شهرياً", "Up to 500 invoices/month"),
                ("فرع ومستودع رئيسي واحد", "1 branch & single warehouse"),
                ("إصدار فواتير ضريبية مبسطة (B2C) مع QR كود TLV", "Simplified Tax Invoices (B2C) with TLV QR Code"),
                ("سندات القبض والصرف الأساسية", "Basic receipt & payment vouchers"),
                ("شجرة حسابات مبسطة (4 مستويات)", "Standard chart of accounts (4 levels)"),
                ("دعم فني عبر البريد وتحديثات دورية", "Email support & regular updates"),
            ]),
        Plan(
            "professional", 2, "باقة الشركات المتقدمة (Professional)", "Professional Business Plan",
            "الخيار الأكثر طلباً للشركات والمؤسسات المتوسطة مع دعم كامل للربط والتكامل ZATCA ومحاسبة التكاليف.",
            "Most popular choice for growing businesses with full ZATCA Phase 2 clearance and costing engine.",
            499, 4990, true, "الأكثر طلباً ⭐", "Most Popular ⭐", 10, null, 3,
            [
                ("حتى 10 مستخدمين مع إدارة أدوار متقدمة (RBAC)", "Up to 10 users with advanced RBAC permissions"),
                ("فواتير مبيعات ومشتريات غير محدودة شهرياً", "Unlimited invoices per month"),
                ("إدارة حتى 3 فروع ومستودعات متعددة", "Up to 3 branches & multi-warehouse inventory"),
                ("ربط وتكامل مع هيئة الزكاة ZATCA Phase 2 (فواتير ضريبية B2B واعتماد لحظي)", "ZATCA Phase 2 Integration (B2B Clearance & B2C Reporting)"),
                ("محرك حساب متوسط التكلفة المرجح المتحرك (Moving Average Costing)", "Weighted Moving Average costing engine"),
                ("شجرة حسابات احترافية مرنة والقيود اليومية الآلية", "Enterprise chart of accounts with auto journal entries"),
                ("تقارير مالية تفصيلية (قائمة الدخل، الميزانية العمومية، إقرار الضريبة)", "Financial reports (P&L, Balance Sheet, VAT Return)"),
                ("دعم فني سريع عبر الواتساب والهاتف", "Priority phone & WhatsApp support"),
            ]),
        Plan(
            "enterprise", 3, "باقة المجموعات والمؤسسات (Enterprise)", "Enterprise Corporate Plan",
            "حل متكامل ومخصص للمجموعات التجارية والشركات الكبرى وفروعها مع بنية تحتية مخصصة وربط API مفتوح.",
            "Custom corporate solution for large holdings and chains with dedicated infrastructure and API webhooks.",
            999, 9990, false, "للمجموعات الكبرى", "Corporate & Holding", null, null, null,
            [
                ("عدد غير محدود من المستخدمين والمشرفين", "Unlimited users and branch supervisors"),
                ("فروع ومستودعات ومراكز تكلفة غير محدودة", "Unlimited branches, warehouses & cost centers"),
                ("ربط برمجي كامل (RESTful API Webhooks) مع المتاجر ونقاط البيع", "Full RESTful API & POS webhooks integration"),
                ("ربط تلقائي بالكامل مع بوابة Fatoora ZATCA وتخزين سحابي للـ CSID", "Automated Fatoora ZATCA portal with cloud CSID storage"),
                ("تعدد العملات وسعر الصرف التلقائي", "Multi-currency support with auto FX rates"),
                ("تقارير تحليلية ومؤشرات أداء مالية متقدمة وتصدير مخصص", "Advanced financial BI dashboards & custom export"),
                ("خادم وقاعدة بيانات مستقلة عالية الأداء ونسخ احتياطي فوري", "Dedicated high-performance tenant database & real-time backup"),
                ("مدير حساب محاسبي معتمد مخصص ودعم على مدار الساعة 24/7", "Dedicated account manager & 24/7 priority SLA"),
            ]),
    ];

    /// <summary>Idempotent upsert of roles, screens and plans (plan features are replaced).</summary>
    public static async Task UpsertAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.Code, cancellationToken);
        foreach (var role in Roles)
        {
            if (roles.TryGetValue(role.Code, out var existing))
            {
                existing.NameAr = role.NameAr;
                existing.NameEn = role.NameEn;
                existing.SortOrder = role.SortOrder;
            }
            else
            {
                db.Roles.Add(new CatalogRole { Code = role.Code, NameAr = role.NameAr, NameEn = role.NameEn, SortOrder = role.SortOrder });
            }
        }

        var screens = await db.Screens.ToDictionaryAsync(s => s.Id, cancellationToken);
        foreach (var screen in Screens)
        {
            if (screens.TryGetValue(screen.Id, out var existing))
            {
                existing.NameAr = screen.NameAr;
                existing.NameEn = screen.NameEn;
                existing.SortOrder = screen.SortOrder;
            }
            else
            {
                db.Screens.Add(new CatalogScreen { Id = screen.Id, NameAr = screen.NameAr, NameEn = screen.NameEn, SortOrder = screen.SortOrder });
            }
        }

        var plans = await db.Plans.Include(p => p.Features).ToDictionaryAsync(p => p.Code, cancellationToken);
        foreach (var plan in Plans())
        {
            if (!plans.TryGetValue(plan.Code, out var existing))
            {
                db.Plans.Add(plan);
                continue;
            }

            db.Entry(existing).CurrentValues.SetValues(plan);
            existing.Features.Clear();
            existing.Features.AddRange(plan.Features);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static SubscriptionPlan Plan(
        string code,
        int sortOrder,
        string nameAr,
        string nameEn,
        string descriptionAr,
        string descriptionEn,
        decimal monthly,
        decimal yearly,
        bool popular,
        string badgeAr,
        string badgeEn,
        int? maxUsers,
        int? maxInvoices,
        int? maxBranches,
        (string Ar, string En)[] features) =>
        new()
        {
            Code = code,
            SortOrder = sortOrder,
            NameAr = nameAr,
            NameEn = nameEn,
            DescriptionAr = descriptionAr,
            DescriptionEn = descriptionEn,
            PriceMonthly = monthly,
            PriceYearly = yearly,
            IsPopular = popular,
            BadgeAr = badgeAr,
            BadgeEn = badgeEn,
            MaxUsers = maxUsers,
            MaxInvoicesPerMonth = maxInvoices,
            MaxBranches = maxBranches,
            Features = features
                .Select((f, i) => new SubscriptionPlanFeature { PlanCode = code, FeatureAr = f.Ar, FeatureEn = f.En, SortOrder = i + 1 })
                .ToList(),
        };
}
