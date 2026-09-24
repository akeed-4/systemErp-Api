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
    public const string Receivables = "112";     // الأب: حسابات العملاء الفرعية تحته
    public const string InputVat = "1131";
    public const string Inventory = "1141";
    public const string VehicleInventory = "1142";
    public const string Payables = "211";        // الأب: حسابات الموردين الفرعية تحته
    public const string AccruedLandedCosts = "212"; // مستحقات جمارك وموانئ (تكاليف محمّلة على المركبات)
    public const string OutputVat = "213";
    public const string Revenue = "411";
    public const string CarSalesRevenue = "412";
    public const string Cogs = "511";
    public const string CarCogs = "512";
    public const string InventoryAdjustment = "513";

    public static readonly (string Code, string Ar, string En, AccountCategory Type, string? Parent)[] Chart =
    {
        ("1",    "الأصول",                          "Assets",                    AccountCategory.Asset,     null),
        ("11",   "الأصول المتداولة",                "Current Assets",            AccountCategory.Asset,     "1"),
        ("111",  "النقدية والبنوك",                 "Cash and Banks",            AccountCategory.Asset,     "11"),
        ("1111", "الصندوق",                         "Cash on Hand",              AccountCategory.Asset,     "111"),
        ("1112", "البنوك (حساب افتراضي)",           "Default Bank Account",      AccountCategory.Asset,     "111"),
        ("112",  "العملاء",                         "Accounts Receivable",       AccountCategory.Asset,     "11"),
        ("113",  "ضريبة وأرصدة مدينة",             "Tax and Other Receivables", AccountCategory.Asset,     "11"),
        ("1131", "ضريبة القيمة المضافة - مدخلات",  "Input VAT",                 AccountCategory.Asset,     "113"),
        ("114",  "المخزون",                         "Inventory",                 AccountCategory.Asset,     "11"),
        ("1141", "مخزون البضاعة",                   "Merchandise Inventory",     AccountCategory.Asset,     "114"),
        ("1142", "مخزون السيارات",                  "Vehicle Inventory",         AccountCategory.Asset,     "114"),
        ("12",   "الأصول الثابتة",                  "Fixed Assets",              AccountCategory.Asset,     "1"),
        ("2",    "الخصوم",                          "Liabilities",               AccountCategory.Liability, null),
        ("21",   "الخصوم المتداولة",                "Current Liabilities",       AccountCategory.Liability, "2"),
        ("211",  "الموردون",                        "Accounts Payable",          AccountCategory.Liability, "21"),
        ("212",  "مستحقات جمارك وموانئ",           "Accrued Customs and Port Fees", AccountCategory.Liability, "21"),
        ("213",  "ضريبة القيمة المضافة - مخرجات",  "Output VAT",                AccountCategory.Liability, "21"),
        ("3",    "حقوق الملكية",                    "Equity",                    AccountCategory.Equity,    null),
        ("31",   "رأس المال",                       "Capital",                   AccountCategory.Equity,    "3"),
        ("32",   "الأرباح المبقاة",                 "Retained Earnings",         AccountCategory.Equity,    "3"),
        ("4",    "الإيرادات",                       "Revenue",                   AccountCategory.Revenue,   null),
        ("41",   "إيرادات المبيعات",                "Sales Revenue Group",       AccountCategory.Revenue,   "4"),
        ("411",  "إيرادات المبيعات والعقود",        "Sales and Contract Revenue",AccountCategory.Revenue,   "41"),
        ("412",  "إيرادات بيع السيارات",            "Car Sales Revenue",         AccountCategory.Revenue,   "41"),
        ("5",    "المصروفات",                       "Expenses",                  AccountCategory.Expense,   null),
        ("51",   "تكلفة المبيعات",                  "Cost of Sales",             AccountCategory.Expense,   "5"),
        ("511",  "تكلفة البضاعة المباعة",           "Cost of Goods Sold",        AccountCategory.Expense,   "51"),
        ("512",  "تكلفة السيارات المباعة",          "Cost of Cars Sold",         AccountCategory.Expense,   "51"),
        ("513",  "فروقات جرد المخزون",              "Inventory Adjustments",     AccountCategory.Expense,   "51"),
        ("52",   "المصروفات التشغيلية",             "Operating Expenses",        AccountCategory.Expense,   "5"),
    };
}
