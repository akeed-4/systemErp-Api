using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أكواد الحسابات الافتراضية في شجرة الحسابات المُهيَّأة لكل منشأة جديدة. تطابق الأكواد التي تستخدمها
/// الواجهة (112 عملاء، 211 موردون، 213 ضريبة مخرجات، 411 إيرادات...). مكانها الوحيد هنا، والخدمات
/// تقرأ منها بدل تكرار الأكواد في كل وحدة.
/// </summary>
public static class DefaultAccounts
{
    public const string Cash = "1111";
    public const string Banks = "111";           // الأب: حسابات البنوك الفرعية تحته
    public const string DefaultBank = "1112";
    public const string PaymentGatewayReceivable = "1113"; // تحصيلات بوابة الدفع الإلكتروني (Paymob) حتى تسويتها للبنك
    public const string Receivables = "112";     // الأب: حسابات العملاء الفرعية تحته
    public const string InputVat = "1131";
    public const string Inventory = "1141";
    public const string VehicleInventory = "1142";
    public const string FixedAssets = "121";          // الأصول الثابتة المادية (الحساب الافتراضي لأصل جديد)
    public const string AccumulatedDepreciation = "122"; // مجمع الإهلاك
    public const string Payables = "211";        // الأب: حسابات الموردين الفرعية تحته
    public const string AccruedLandedCosts = "212"; // مستحقات جمارك وموانئ (تكاليف محمّلة على المركبات)
    public const string OutputVat = "213";
    public const string SalariesPayable = "214";          // رواتب مستحقة للموظفين (صافي المسير حتى صرفه)
    public const string SocialInsurancePayable = "215";   // تأمينات اجتماعية مستحقة (حصة الموظف والمنشأة حتى سدادها)
    public const string EmployeeLoans = "115";            // سلف وقروض الموظفين (ذمم مدينة حتى خصمها من الرواتب)
    public const string EndOfServiceExpense = "528";      // مكافأة نهاية الخدمة
    public const string SalariesExpense = "526";          // رواتب وأجور
    public const string SocialInsuranceExpense = "527";   // حصة المنشأة في التأمينات الاجتماعية
    public const string OpeningBalanceEquity = "33"; // مقابل الأرصدة الافتتاحية للعملاء والموردين والبنوك
    public const string RetainedEarnings = "32";    // الأرباح المبقاة (يُرحَّل إليها صافي السنة عند إقفالها)
    public const string Revenue = "411";
    public const string CarSalesRevenue = "412";
    public const string Cogs = "511";
    public const string CarCogs = "512";
    public const string InventoryAdjustment = "513";
    public const string PurchasePriceVariance = "514"; // فرق سعر مرتجع المشتريات عن تكلفة المخزون وقت خروجه
    public const string AssetDisposalGain = "422";  // أرباح بيع/استبعاد الأصول الثابتة
    public const string AssetDisposalLoss = "525";  // خسائر بيع/استبعاد الأصول الثابتة
    public const string CashOverage = "421";        // زيادة نقدية الصندوق عند إغلاق الوردية
    public const string PettyCashExpenses = "521";  // المصروفات النثرية المصروفة من درج الكاشير
    public const string CashShortage = "522";       // عجز نقدية الصندوق عند إغلاق الوردية
    public const string DepreciationExpense = "523"; // مصروف إهلاك الأصول الثابتة (الحساب الافتراضي لأصل جديد)
    public const string PurchasedServices = "524";   // خدمات ومصروفات مشتراة بفاتورة (بنود غير مخزنية)

    public static readonly (string Code, string Ar, string En, AccountCategory Type, string? Parent)[] Chart =
    {
        ("1",    "الأصول",                          "Assets",                    AccountCategory.Asset,     null),
        ("11",   "الأصول المتداولة",                "Current Assets",            AccountCategory.Asset,     "1"),
        ("111",  "النقدية والبنوك",                 "Cash and Banks",            AccountCategory.Asset,     "11"),
        ("1111", "الصندوق",                         "Cash on Hand",              AccountCategory.Asset,     "111"),
        ("1112", "البنوك (حساب افتراضي)",           "Default Bank Account",      AccountCategory.Asset,     "111"),
        ("1113", "تحصيلات بوابة الدفع الإلكتروني", "Payment Gateway Receivable", AccountCategory.Asset, "111"),
        ("112",  "العملاء",                         "Accounts Receivable",       AccountCategory.Asset,     "11"),
        ("113",  "ضريبة وأرصدة مدينة",             "Tax and Other Receivables", AccountCategory.Asset,     "11"),
        ("1131", "ضريبة القيمة المضافة - مدخلات",  "Input VAT",                 AccountCategory.Asset,     "113"),
        ("114",  "المخزون",                         "Inventory",                 AccountCategory.Asset,     "11"),
        ("1141", "مخزون البضاعة",                   "Merchandise Inventory",     AccountCategory.Asset,     "114"),
        ("1142", "مخزون السيارات",                  "Vehicle Inventory",         AccountCategory.Asset,     "114"),
        ("115",  "سلف وقروض الموظفين",              "Employee Loans and Advances", AccountCategory.Asset,   "11"),
        ("12",   "الأصول الثابتة",                  "Fixed Assets",              AccountCategory.Asset,     "1"),
        ("121",  "الأصول الثابتة المادية",          "Tangible Fixed Assets",     AccountCategory.Asset,     "12"),
        ("122",  "مجمع الإهلاك",                    "Accumulated Depreciation",  AccountCategory.Asset,     "12"),
        ("2",    "الخصوم",                          "Liabilities",               AccountCategory.Liability, null),
        ("21",   "الخصوم المتداولة",                "Current Liabilities",       AccountCategory.Liability, "2"),
        ("211",  "الموردون",                        "Accounts Payable",          AccountCategory.Liability, "21"),
        ("212",  "مستحقات جمارك وموانئ",           "Accrued Customs and Port Fees", AccountCategory.Liability, "21"),
        ("213",  "ضريبة القيمة المضافة - مخرجات",  "Output VAT",                AccountCategory.Liability, "21"),
        ("214",  "رواتب مستحقة",                    "Salaries Payable",          AccountCategory.Liability, "21"),
        ("215",  "تأمينات اجتماعية مستحقة",         "Social Insurance Payable",  AccountCategory.Liability, "21"),
        ("3",    "حقوق الملكية",                    "Equity",                    AccountCategory.Equity,    null),
        ("31",   "رأس المال",                       "Capital",                   AccountCategory.Equity,    "3"),
        ("32",   "الأرباح المبقاة",                 "Retained Earnings",         AccountCategory.Equity,    "3"),
        ("33",   "الأرصدة الافتتاحية",              "Opening Balance Equity",    AccountCategory.Equity,    "3"),
        ("4",    "الإيرادات",                       "Revenue",                   AccountCategory.Revenue,   null),
        ("41",   "إيرادات المبيعات",                "Sales Revenue Group",       AccountCategory.Revenue,   "4"),
        ("411",  "إيرادات المبيعات والعقود",        "Sales and Contract Revenue",AccountCategory.Revenue,   "41"),
        ("412",  "إيرادات بيع السيارات",            "Car Sales Revenue",         AccountCategory.Revenue,   "41"),
        ("42",   "إيرادات أخرى",                    "Other Revenue",             AccountCategory.Revenue,   "4"),
        ("421",  "زيادة نقدية الصندوق",             "Cash Overage",              AccountCategory.Revenue,   "42"),
        ("422",  "أرباح استبعاد الأصول الثابتة",    "Gain on Disposal of Fixed Assets", AccountCategory.Revenue, "42"),
        ("5",    "المصروفات",                       "Expenses",                  AccountCategory.Expense,   null),
        ("51",   "تكلفة المبيعات",                  "Cost of Sales",             AccountCategory.Expense,   "5"),
        ("511",  "تكلفة البضاعة المباعة",           "Cost of Goods Sold",        AccountCategory.Expense,   "51"),
        ("512",  "تكلفة السيارات المباعة",          "Cost of Cars Sold",         AccountCategory.Expense,   "51"),
        ("513",  "فروقات جرد المخزون",              "Inventory Adjustments",     AccountCategory.Expense,   "51"),
        ("514",  "فروق أسعار المشتريات",            "Purchase Price Variance",   AccountCategory.Expense,   "51"),
        ("52",   "المصروفات التشغيلية",             "Operating Expenses",        AccountCategory.Expense,   "5"),
        ("521",  "مصروفات نثرية من الصندوق",        "Petty Cash Expenses",       AccountCategory.Expense,   "52"),
        ("522",  "عجز نقدية الصندوق",               "Cash Shortage",             AccountCategory.Expense,   "52"),
        ("523",  "مصروف إهلاك الأصول الثابتة",      "Depreciation Expense",      AccountCategory.Expense,   "52"),
        ("524",  "خدمات ومصروفات مشتراة",           "Purchased Services and Expenses", AccountCategory.Expense, "52"),
        ("525",  "خسائر استبعاد الأصول الثابتة",    "Loss on Disposal of Fixed Assets", AccountCategory.Expense, "52"),
        ("526",  "رواتب وأجور",                     "Salaries and Wages",        AccountCategory.Expense,   "52"),
        ("527",  "تأمينات اجتماعية - حصة المنشأة",  "Social Insurance - Employer Share", AccountCategory.Expense, "52"),
        ("528",  "مكافأة نهاية الخدمة",             "End of Service Benefits",  AccountCategory.Expense,   "52"),
    };

    /// <summary>طبيعة الحساب: الأصول والمصروفات مدينة، عدا مجمع الإهلاك (حساب مقابل للأصل) فطبيعته دائنة.</summary>
    public static bool IsDebitNature(string code, AccountCategory type)
        => code != AccumulatedDepreciation && type is AccountCategory.Asset or AccountCategory.Expense;

    /// <summary>
    /// ينشئ حسابات النظام الناقصة (مع آبائها) للمنشأة الحالية: المنشآت القديمة هُيّئت قبل إضافة بعض الحسابات للشجرة.
    /// </summary>
    public static async Task EnsureAsync(ErpDbContext db, CancellationToken ct, params string[] codes)
    {
        var chart = Chart.ToDictionary(a => a.Code);
        var needed = new List<string>();
        foreach (var code in codes)
            for (var c = code; c != null && chart.ContainsKey(c); c = chart[c].Parent)
                if (!needed.Contains(c)) needed.Add(c);
        var existing = await db.Set<Account>().Where(a => needed.Contains(a.Code)).ToDictionaryAsync(a => a.Code, ct);
        var missing = needed.Where(c => !existing.ContainsKey(c)).ToList();
        if (missing.Count == 0) return;

        var currency = await db.Set<Currency>().Where(c => c.IsBaseCurrency).Select(c => c.Code).FirstOrDefaultAsync(ct) ?? "SAR";
        foreach (var code in missing.OrderBy(c => c.Length))
        {
            var (_, ar, en, type, parent) = chart[code];
            var level = parent == null ? 1 : existing[parent].Level + 1; // الآباء أقصر كوداً فأُنشئوا/وُجدوا قبله
            var account = new Account
            {
                Code = code, NameAr = ar, NameEn = en, Type = type, ParentCode = parent, Level = level,
                IsDebitNature = IsDebitNature(code, type), IsSystem = true, Currency = currency,
            };
            db.Add(account);
            existing[code] = account;
        }
        await db.SaveChangesAsync(ct);
    }
}
