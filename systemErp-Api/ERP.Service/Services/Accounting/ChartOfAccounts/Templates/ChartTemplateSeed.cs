namespace ERP.Service.Services.Accounting;

/// <summary>
/// القوالب الافتراضية (تُزرع في القاعدة المشتركة عند أول قراءة للقوالب إن كان الجدول فارغاً، ثم يديرها مدير المنصة).
/// مشتقة من الشجرة الافتراضية القديمة <see cref="DefaultAccounts.Chart"/> بأرقامها وأسمائها: القالب الأساسي + التجارة العامة +
/// موديولات المعارض والموارد البشرية والمقاولات = الشجرة القديمة نفسها، فلا تتغير حسابات النظام ولا معانيها.
/// حسابات الأدوار بلا رقم في القالب (مثل حسابات المقاولات) تُرقَّم عند التطبيق تالية لإخوتها، كما كانت تفعل تهيئة حسابات العقود.
/// </summary>
internal static class ChartTemplateSeed
{
    public const string BaseKey = "base";
    public static string ActivityKey(string activity) => "activity." + activity;
    public static string ModuleKey(string module) => "module." + module;

    /// <summary>الحسابات التي يفترضها التأسيس نفسه (الفرع والموقع الرئيسي بحساباته): إلزامية في القالب الأساسي.</summary>
    public static readonly string[] SeedRequiredCodes =
    {
        "1", "11", DefaultAccounts.Banks, DefaultAccounts.DefaultBank, DefaultAccounts.CashBoxes, DefaultAccounts.Cash,
        DefaultAccounts.Receivables, "113", DefaultAccounts.InputVat, DefaultAccounts.InventoryGroup, DefaultAccounts.Inventory,
        "12", DefaultAccounts.FixedAssets, DefaultAccounts.AccumulatedDepreciation,
        "2", "21", DefaultAccounts.Payables, DefaultAccounts.OutputVat,
        "3", "31", DefaultAccounts.RetainedEarnings, DefaultAccounts.OpeningBalanceEquity,
        "4", "41", DefaultAccounts.Revenue, DefaultAccounts.OtherRevenue,
        "5", "51", DefaultAccounts.Cogs, DefaultAccounts.InventoryAdjustment, "52",
    };

    private static readonly string[] BaseCodes =
    {
        "1", "11", "110", "1101", "1102", "111", "1111", "112", "113", "1131", "114", "1141", "117", "12", "121", "122",
        "2", "21", "211", "213", "217", "3", "31", "32", "33",
        "4", "41", "411", "42", "421", "422", "5", "51", "511", "513", "52", "521", "522", "523", "524", "525",
    };

    /// <summary>حسابات حركة البضاعة غير الأساسية (تُنشأ أيضاً عند أول استخدام بأرقامها المحجوزة لمن لا يملكها).</summary>
    private static readonly string[] TradingCodes = { "514", "516", "531", "532", "424", "1145" };

    private static readonly string[] CarCodes =
        { "1142", "1143", "1144", "116", "118", "119", "212", "216", "412", "413", "423", "512", "515", "529", "530" };

    private static readonly string[] HrCodes = { "115", "214", "215", "526", "527", "528" };

    /// <summary>
    /// مفتاح الحساب القديم: المفتاح المحجوز لرقمه، وإلا مفتاح الدور إن انفرد بهذا الافتراضي (حساب يخدم أكثر من دور لا يحمل مفتاح أحدها).
    /// </summary>
    private static readonly Dictionary<string, string> KeyOfReserved = AccountSemanticKeys.All
        .Where(d => d.DefaultCode != null)
        .GroupBy(d => d.DefaultCode!)
        .Select(g => (Code: g.Key, Def: g.FirstOrDefault(d => d.Store == AccountSemanticKeys.ReservedStore) ?? (g.Count() == 1 ? g.First() : null)))
        .Where(x => x.Def != null)
        .ToDictionary(x => x.Code, x => x.Def!.Key);

    private static ChartTemplateAccount Legacy(string code, int order)
    {
        var a = DefaultAccounts.Chart.First(x => x.Code == code);
        return new ChartTemplateAccount
        {
            Id = Guid.NewGuid(), Code = a.Code, NameAr = a.Ar, NameEn = a.En, ParentCode = a.Parent, Type = a.Type,
            IsGroup = DefaultAccounts.IsGroup(a.Code), SemanticKey = KeyOfReserved.GetValueOrDefault(a.Code),
            IsRequired = SeedRequiredCodes.Contains(a.Code), SortOrder = order,
        };
    }

    /// <summary>حساب دور بلا رقم ثابت: يُرقَّم تالياً لإخوته تحت الأب عند التطبيق.</summary>
    private static ChartTemplateAccount Role(string key, string parent, AccountCategory type, string ar, string en, int order) => new()
    {
        Id = Guid.NewGuid(), Code = null, NameAr = ar, NameEn = en, ParentCode = parent, Type = type,
        SemanticKey = key, IsRequired = true, SortOrder = order,
    };

    private static ChartTemplateAccount New(string? code, string parent, AccountCategory type, string ar, string en, string key, int order,
        bool isGroup = false) => new()
    {
        Id = Guid.NewGuid(), Code = code, NameAr = ar, NameEn = en, ParentCode = parent, Type = type, IsGroup = isGroup,
        SemanticKey = key, IsRequired = false, SortOrder = order,
    };

    private static ChartTemplate Template(string key, string ar, string en, ChartTemplateKind kind, int order, IEnumerable<ChartTemplateAccount> accounts,
        string? activity = null, string? module = null)
    {
        var t = new ChartTemplate
        {
            Id = Guid.NewGuid(), Key = key, NameAr = ar, NameEn = en, Kind = kind, ActivityKey = activity, ModuleKey = module,
            SortOrder = order, UpdatedAt = DateTime.UtcNow, UpdatedBy = "system",
        };
        foreach (var a in accounts) { a.TemplateId = t.Id; t.Accounts.Add(a); }
        return t;
    }

    private static IEnumerable<ChartTemplateAccount> LegacySet(IEnumerable<string> codes) => codes.Select((c, i) => Legacy(c, i + 1));

    // حسابات أدوار المقاولات: بجوار مماثلاتها كما في تهيئة حسابات العقود (الأصول بجوار ضريبة المدخلات، الالتزامات بجوار ضريبة
    // المخرجات، الإيرادات تحت الإيرادات الأخرى، المصروفات تحت التشغيلية)، وبترتيب أدوارها نفسه فتأخذ التركيبة القديمة الأرقام نفسها
    private static IEnumerable<ChartTemplateAccount> ConstructionAccounts()
    {
        var i = 0;
        yield return Legacy(DefaultAccounts.ProjectMaterialsCost, ++i);
        yield return Role("Construction.AdvancesReceived", "21", AccountCategory.Liability, "دفعات مقدمة مستلمة على العقود", "Contract Advances Received", ++i);
        yield return Role("Construction.RetentionPayable", "21", AccountCategory.Liability, "محتجزات ضمان مستحقة لمقاولي الباطن", "Retention Payable to Subcontractors", ++i);
        yield return Role("Construction.RetentionReceivable", "113", AccountCategory.Asset, "محتجزات ضمان لدى العملاء", "Retention Receivable from Customers", ++i);
        yield return Role("Construction.AdvancesPaid", "113", AccountCategory.Asset, "دفعات مقدمة مدفوعة لمقاولي الباطن", "Advances Paid to Subcontractors", ++i);
        yield return Role("Construction.PenaltiesIncome", "42", AccountCategory.Revenue, "إيراد غرامات العقود", "Contract Penalties Income", ++i);
        yield return Role("Construction.PenaltiesExpense", "52", AccountCategory.Expense, "غرامات العقود", "Contract Penalties Expense", ++i);
        yield return Role("Construction.OtherDeductions", "52", AccountCategory.Expense, "خصومات العقود الأخرى", "Other Contract Deductions", ++i);
        yield return Role("Construction.WorkInProgress", "113", AccountCategory.Asset, "مواد مشاريع تحت التنفيذ", "Project Materials Under Construction", ++i);
        yield return Role("Construction.ContractCostOfRevenue", "52", AccountCategory.Expense, "تكلفة إيرادات العقود", "Contract Cost of Revenue", ++i);
        yield return Role("Construction.LaborCost", "52", AccountCategory.Expense, "تكلفة عمالة المشاريع", "Project Labor Cost", ++i);
        yield return Role("Construction.EquipmentCost", "52", AccountCategory.Expense, "تكلفة معدات المشاريع", "Project Equipment Cost", ++i);
        yield return Role("Construction.AbsorbedCost", "52", AccountCategory.Expense, "تكاليف محمّلة على المشاريع", "Costs Absorbed by Projects", ++i);
        yield return Role("Construction.RentedEquipmentCost", "52", AccountCategory.Expense, "تكلفة معدات مستأجرة للمشاريع", "Project Rented Equipment Cost", ++i);
        yield return Role("Construction.AccruedRental", "21", AccountCategory.Liability, "إيجار معدات مستحق", "Accrued Equipment Rental", ++i);
        yield return Role("Construction.GoodsReceivedNotInvoiced", "21", AccountCategory.Liability, "بضاعة مستلمة لم تُفوتر", "Goods Received Not Invoiced", ++i);
    }

    private static IEnumerable<ChartTemplateAccount> WorkshopAccounts()
    {
        yield return Role("Workshop.WorkInProgress", "113", AccountCategory.Asset, "أعمال ورشة تحت التنفيذ", "Workshop Work in Progress", 1);
        yield return Role("Workshop.AbsorbedLabor", "52", AccountCategory.Expense, "عمالة ورشة محمّلة", "Workshop Absorbed Labor", 2);
        yield return Role("Workshop.CostOfRevenue", "52", AccountCategory.Expense, "تكلفة إيرادات الورشة", "Workshop Cost of Revenue", 3);
        yield return Role("Workshop.WarrantyExpense", "52", AccountCategory.Expense, "مصروف ضمان أعمال الورشة", "Workshop Warranty Expense", 4);
    }

    private static IEnumerable<ChartTemplateAccount> RentalAccounts()
    {
        yield return Role("Rental.Revenue", "42", AccountCategory.Revenue, "إيراد تأجير المعدات", "Equipment Rental Revenue", 1);
        yield return Role("Rental.CompensationIncome", "42", AccountCategory.Revenue, "إيراد تعويضات وغرامات التأجير", "Rental Compensation & Penalty Income", 2);
        yield return Role("Rental.Deposits", "21", AccountCategory.Liability, "تأمينات عملاء التأجير", "Rental Customer Deposits", 3);
    }

    private static IEnumerable<ChartTemplateAccount> TailoringAccounts()
    {
        yield return Role("Tailoring.WorkInProgress", "113", AccountCategory.Asset, "أعمال خياطة تحت التنفيذ", "Tailoring Work in Progress", 1);
        yield return Role("Tailoring.AbsorbedLabor", "52", AccountCategory.Expense, "أجور خياطة محمّلة", "Tailoring Absorbed Labor", 2);
        yield return Role("Tailoring.CostOfRevenue", "52", AccountCategory.Expense, "تكلفة إيرادات الخياطة", "Tailoring Cost of Revenue", 3);
        yield return Role("Tailoring.AdvancesReceived", "21", AccountCategory.Liability, "دفعات مقدمة من عملاء الخياطة", "Tailoring Customer Advances", 4);
    }

    private static IEnumerable<ChartTemplateAccount> ServicesAccounts()
    {
        // إيراد الخدمات هو حساب الإيراد الافتراضي نفسه (411) باسم النشاط: الفواتير تُرحَّل عليه كما هي
        var revenue = Legacy(DefaultAccounts.Revenue, 1);
        revenue.NameAr = "إيرادات الخدمات"; revenue.NameEn = "Service Revenue";
        yield return revenue;
        yield return New(null, "51", AccountCategory.Expense, "تكاليف تقديم الخدمات", "Cost of Services Rendered", "Services.CostOfServices", 2);
        yield return New(null, "52", AccountCategory.Expense, "مصروفات تشغيل الخدمات", "Service Operating Expenses", "Services.OperatingExpenses", 3);
    }

    private static IEnumerable<ChartTemplateAccount> ManufacturingAccounts()
    {
        var i = 0;
        foreach (var a in LegacySet(TradingCodes)) { a.SortOrder = ++i; yield return a; }
        yield return New(null, DefaultAccounts.InventoryGroup, AccountCategory.Asset, "مخزون المواد الخام", "Raw Materials Inventory", "Manufacturing.RawMaterials", ++i);
        yield return New(null, DefaultAccounts.InventoryGroup, AccountCategory.Asset, "إنتاج تحت التشغيل", "Work in Process", "Manufacturing.WorkInProcess", ++i);
        yield return New(null, DefaultAccounts.InventoryGroup, AccountCategory.Asset, "مخزون الإنتاج التام", "Finished Goods Inventory", "Manufacturing.FinishedGoods", ++i);
        yield return New(null, "51", AccountCategory.Expense, "تكاليف التصنيع", "Manufacturing Costs", "Manufacturing.Costs", ++i);
        yield return New(null, "51", AccountCategory.Expense, "تكاليف صناعية غير مباشرة", "Manufacturing Overheads", "Manufacturing.Overheads", ++i);
    }

    public static List<ChartTemplate> Build()
    {
        var list = new List<ChartTemplate>
        {
            Template(BaseKey, "القالب الأساسي المشترك", "Common Base Chart", ChartTemplateKind.Base, 1, LegacySet(BaseCodes)),
            Template(ActivityKey(BusinessActivities.Trading), "التجارة العامة", "General Trading", ChartTemplateKind.Activity, 10,
                LegacySet(TradingCodes), activity: BusinessActivities.Trading),
            Template(ActivityKey(BusinessActivities.Construction), "المقاولات والإنشاءات", "Contracting & Construction", ChartTemplateKind.Activity, 11,
                ConstructionAccounts(), activity: BusinessActivities.Construction),
            Template(ActivityKey(BusinessActivities.CarShowroom), "معارض السيارات", "Car Showrooms", ChartTemplateKind.Activity, 12,
                LegacySet(TradingCodes.Concat(CarCodes)), activity: BusinessActivities.CarShowroom),
            Template(ActivityKey(BusinessActivities.Services), "الخدمات", "Services", ChartTemplateKind.Activity, 13,
                ServicesAccounts(), activity: BusinessActivities.Services),
            Template(ActivityKey(BusinessActivities.Manufacturing), "الصناعة", "Manufacturing", ChartTemplateKind.Activity, 14,
                ManufacturingAccounts(), activity: BusinessActivities.Manufacturing),
            Template(ModuleKey(PlatformModules.CarShowroom), "موديول معارض السيارات", "Car Showroom Module", ChartTemplateKind.Module, 20,
                LegacySet(CarCodes), module: PlatformModules.CarShowroom),
            Template(ModuleKey(PlatformModules.Hr), "موديول الموارد البشرية", "HR Module", ChartTemplateKind.Module, 21,
                LegacySet(HrCodes), module: PlatformModules.Hr),
            Template(ModuleKey(PlatformModules.Construction), "موديول المقاولات", "Construction Module", ChartTemplateKind.Module, 22,
                ConstructionAccounts(), module: PlatformModules.Construction),
            Template(ModuleKey(PlatformModules.Workshop), "موديول ورش السيارات", "Workshop Module", ChartTemplateKind.Module, 23,
                WorkshopAccounts(), module: PlatformModules.Workshop),
            Template(ModuleKey(PlatformModules.EquipmentRental), "موديول تأجير المعدات", "Equipment Rental Module", ChartTemplateKind.Module, 24,
                RentalAccounts(), module: PlatformModules.EquipmentRental),
            Template(ModuleKey(PlatformModules.Tailoring), "موديول الخياطة والتفصيل", "Tailoring Module", ChartTemplateKind.Module, 25,
                TailoringAccounts(), module: PlatformModules.Tailoring),
        };
        return list;
    }
}
