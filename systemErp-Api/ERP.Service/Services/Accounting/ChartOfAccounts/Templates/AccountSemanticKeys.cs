namespace ERP.Service.Services.Accounting;

/// <summary>
/// المفاتيح الوظيفية الثابتة للحسابات ومكان ربط كل منها في المنشأة — طبقة أسماء فوق مخازن الربط القائمة، لا مخزن ثانٍ:
/// <list type="bullet">
/// <item><c>setting</c>: دور في <see cref="AccountingSettings"/> (<see cref="PostingAccounts.Roles"/>) تقرؤه خدمات الترحيل كما هي.</item>
/// <item><c>reserved</c>: رقم ثابت يقرؤه الكود مباشرة اليوم (ثوابت DefaultAccounts وافتراضيات المعرض)؛ الحساب يُعرف برقمه المحجوز.</item>
/// <item><c>binding</c>: كل مفتاح آخر (ومنه مفاتيح يضيفها محرر القوالب): <see cref="AccountBinding"/> بمعرّف الحساب.</item>
/// </list>
/// إدخال مفتاح في خدمة قائمة يتم تدريجياً بنقله من reserved إلى binding مع قراءته من <c>IAccountKeyResolver</c>.
/// </summary>
internal static class AccountSemanticKeys
{
    public const string SettingStore = "setting";
    public const string ReservedStore = "reserved";
    public const string BindingStore = "binding";

    public sealed record Definition(string Key, string Store, string? RoleKey, string? DefaultCode, string LabelAr, string LabelEn)
    {
        public PostingAccounts.Role? Role => RoleKey == null ? null : PostingAccounts.Roles.First(r => r.Key == RoleKey);
        public string Label => (Messages.Culture ?? System.Globalization.CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "en" ? LabelEn : LabelAr;
    }

    private static Definition S(string key, string roleKey, string ar, string en)
        => new(key, SettingStore, roleKey, PostingAccounts.Roles.First(r => r.Key == roleKey).Default, ar, en);
    private static Definition R(string key, string code, string ar, string en) => new(key, ReservedStore, null, code, ar, en);

    public static readonly Definition[] All =
    {
        // المخزون والمبيعات والمشتريات والضريبة (إعدادات الحسابات)
        S("Inventory.Stock", nameof(AccountingSettings.InventoryAccountCode), "المخزون", "Inventory"),
        S("Sales.CostOfGoodsSold", nameof(AccountingSettings.CogsAccountCode), "تكلفة البضاعة المباعة", "Cost of goods sold"),
        S("Inventory.Shortage", nameof(AccountingSettings.InventoryShortageAccountCode), "عجز المخزون", "Inventory shortage"),
        S("Inventory.Surplus", nameof(AccountingSettings.InventorySurplusAccountCode), "زيادة المخزون", "Inventory surplus"),
        S("Inventory.IssueExpense", nameof(AccountingSettings.InventoryIssueAccountCode), "مقابل الصرف المخزني", "Inventory issue counter account"),
        S("Inventory.ReceiptWithoutInvoice", nameof(AccountingSettings.InventoryReceiptAccountCode), "مقابل الاستلام المخزني", "Inventory receipt counter account"),
        S("Inventory.OpeningReceipt", nameof(AccountingSettings.InventoryOpeningReceiptAccountCode), "استلام الرصيد الافتتاحي", "Opening stock receipt"),
        S("Purchases.Purchases", nameof(AccountingSettings.PurchasesAccountCode), "المشتريات (الجرد الدوري)", "Purchases (periodic)"),
        S("Purchases.Returns", nameof(AccountingSettings.PurchaseReturnsAccountCode), "مردودات المشتريات", "Purchase returns"),
        S("Inventory.Opening", nameof(AccountingSettings.OpeningInventoryAccountCode), "بضاعة أول المدة", "Opening inventory"),
        S("Inventory.Closing", nameof(AccountingSettings.EndingInventoryAccountCode), "بضاعة آخر المدة", "Closing inventory"),
        S("Sales.Revenue", nameof(AccountingSettings.SalesRevenueAccountCode), "إيرادات المبيعات", "Sales revenue"),
        S("Sales.Returns", nameof(AccountingSettings.SalesReturnsAccountCode), "مردودات المبيعات", "Sales returns"),
        S("Tax.OutputVat", nameof(AccountingSettings.OutputVatAccountCode), "ضريبة المخرجات", "Output VAT"),
        S("Tax.InputVat", nameof(AccountingSettings.InputVatAccountCode), "ضريبة المدخلات", "Input VAT"),
        S("Tax.OutputExcise", nameof(AccountingSettings.OutputExciseAccountCode), "الضريبة الانتقائية", "Excise tax"),

        // المقاولات
        S("Construction.ProjectMaterialCost", nameof(AccountingSettings.InventoryProjectIssueAccountCode), "تكلفة مواد المشاريع", "Project materials cost"),
        S("Construction.WorkInProgress", nameof(AccountingSettings.ProjectMaterialsWipAccountCode), "مواد مشاريع تحت التنفيذ", "Projects work in progress"),
        S("Construction.ContractCostOfRevenue", nameof(AccountingSettings.ContractCostOfRevenueAccountCode), "تكلفة إيرادات العقود", "Contract cost of revenue"),
        S("Construction.LaborCost", nameof(AccountingSettings.ProjectLaborCostAccountCode), "أجور العمالة بالمشاريع", "Project labor cost"),
        S("Construction.EquipmentCost", nameof(AccountingSettings.ProjectEquipmentCostAccountCode), "تكاليف معدات المشاريع", "Project equipment cost"),
        S("Construction.AbsorbedCost", nameof(AccountingSettings.ProjectAbsorbedCostAccountCode), "تكاليف محمّلة على المشاريع", "Costs absorbed by projects"),
        S("Construction.RentedEquipmentCost", nameof(AccountingSettings.ProjectRentedEquipmentCostAccountCode), "تكلفة معدات مستأجرة", "Rented equipment cost"),
        S("Construction.AccruedRental", nameof(AccountingSettings.ProjectAccruedRentalAccountCode), "إيجار معدات مستحق", "Accrued equipment rental"),
        S("Construction.GoodsReceivedNotInvoiced", nameof(AccountingSettings.ProjectGoodsReceivedNotInvoicedAccountCode), "بضاعة مستلمة لم تُفوتر", "Goods received not invoiced"),
        S("Construction.AdvancesReceived", nameof(AccountingSettings.ContractAdvancesReceivedAccountCode), "دفعات مقدمة من العملاء", "Contract advances received"),
        S("Construction.AdvancesPaid", nameof(AccountingSettings.ContractAdvancesPaidAccountCode), "دفعات مقدمة للمقاولين", "Advances paid to subcontractors"),
        S("Construction.RetentionReceivable", nameof(AccountingSettings.ContractRetentionReceivableAccountCode), "محتجزات لدى العملاء", "Retention receivable"),
        S("Construction.RetentionPayable", nameof(AccountingSettings.ContractRetentionPayableAccountCode), "محتجزات المقاولين", "Retention payable"),
        S("Construction.PenaltiesExpense", nameof(AccountingSettings.ContractPenaltiesExpenseAccountCode), "غرامات العقود", "Contract penalties expense"),
        S("Construction.PenaltiesIncome", nameof(AccountingSettings.ContractPenaltiesIncomeAccountCode), "إيراد غرامات العقود", "Contract penalties income"),
        S("Construction.OtherDeductions", nameof(AccountingSettings.ContractOtherDeductionsAccountCode), "خصومات العقود الأخرى", "Other contract deductions"),

        // الورش والتأجير
        S("Workshop.WorkInProgress", nameof(AccountingSettings.WorkshopWipAccountCode), "أعمال ورشة تحت التنفيذ", "Workshop work in progress"),
        S("Workshop.AbsorbedLabor", nameof(AccountingSettings.WorkshopAbsorbedLaborAccountCode), "عمالة ورشة محمّلة", "Workshop absorbed labor"),
        S("Workshop.CostOfRevenue", nameof(AccountingSettings.WorkshopCostOfRevenueAccountCode), "تكلفة إيرادات الورشة", "Workshop cost of revenue"),
        S("Workshop.WarrantyExpense", nameof(AccountingSettings.WorkshopWarrantyExpenseAccountCode), "مصروف ضمان الورشة", "Workshop warranty expense"),
        S("Rental.Revenue", nameof(AccountingSettings.RentalRevenueAccountCode), "إيراد تأجير المعدات", "Equipment rental revenue"),
        S("Rental.CompensationIncome", nameof(AccountingSettings.RentalCompensationIncomeAccountCode), "إيراد تعويضات التأجير", "Rental compensation income"),
        S("Rental.Deposits", nameof(AccountingSettings.RentalDepositsLiabilityAccountCode), "تأمينات عملاء التأجير", "Rental customer deposits"),
        S("Tailoring.WorkInProgress", nameof(AccountingSettings.TailoringWipAccountCode), "أعمال خياطة تحت التنفيذ", "Tailoring work in progress"),
        S("Tailoring.AbsorbedLabor", nameof(AccountingSettings.TailoringAbsorbedLaborAccountCode), "أجور خياطة محمّلة", "Tailoring absorbed labor"),
        S("Tailoring.CostOfRevenue", nameof(AccountingSettings.TailoringCostOfRevenueAccountCode), "تكلفة إيرادات الخياطة", "Tailoring cost of revenue"),
        S("Tailoring.AdvancesReceived", nameof(AccountingSettings.TailoringAdvancesReceivedAccountCode), "دفعات مقدمة من عملاء الخياطة", "Tailoring customer advances"),

        // أرقام محجوزة يقرؤها النظام مباشرة اليوم
        R("Treasury.CashBoxes", DefaultAccounts.CashBoxes, "الصناديق", "Cash boxes"),
        R("Treasury.MainCash", DefaultAccounts.Cash, "الصندوق الرئيسي", "Main cash box"),
        R("Treasury.Banks", DefaultAccounts.Banks, "البنوك", "Banks"),
        R("Treasury.DefaultBank", DefaultAccounts.DefaultBank, "البنك الافتراضي", "Default bank"),
        R("Treasury.PaymentGateway", DefaultAccounts.PaymentGatewayReceivable, "تحصيلات بوابة الدفع", "Payment gateway receivable"),
        R("Parties.Customers", DefaultAccounts.Receivables, "العملاء", "Customers"),
        R("Parties.Suppliers", DefaultAccounts.Payables, "الموردون", "Suppliers"),
        R("Inventory.Group", DefaultAccounts.InventoryGroup, "مجموعة المخزون", "Inventory group"),
        R("Inventory.Adjustment", DefaultAccounts.InventoryAdjustment, "فروقات جرد المخزون", "Inventory adjustments"),
        R("Purchases.PriceVariance", DefaultAccounts.PurchasePriceVariance, "فروق أسعار المشتريات", "Purchase price variance"),
        R("Purchases.Freight", DefaultAccounts.PurchaseFreightExpense, "مصاريف شحن المشتريات", "Purchase freight"),
        R("Purchases.Services", DefaultAccounts.PurchasedServices, "خدمات ومصروفات مشتراة", "Purchased services"),
        R("Assets.Fixed", DefaultAccounts.FixedAssets, "الأصول الثابتة", "Fixed assets"),
        R("Assets.AccumulatedDepreciation", DefaultAccounts.AccumulatedDepreciation, "مجمع الإهلاك", "Accumulated depreciation"),
        R("Assets.DepreciationExpense", DefaultAccounts.DepreciationExpense, "مصروف الإهلاك", "Depreciation expense"),
        R("Assets.DisposalGain", DefaultAccounts.AssetDisposalGain, "أرباح استبعاد الأصول", "Gain on asset disposal"),
        R("Assets.DisposalLoss", DefaultAccounts.AssetDisposalLoss, "خسائر استبعاد الأصول", "Loss on asset disposal"),
        R("Equity.OpeningBalance", DefaultAccounts.OpeningBalanceEquity, "الأرصدة الافتتاحية", "Opening balance equity"),
        R("Equity.RetainedEarnings", DefaultAccounts.RetainedEarnings, "الأرباح المبقاة", "Retained earnings"),
        R("Revenue.Other", DefaultAccounts.OtherRevenue, "الإيرادات الأخرى", "Other revenue"),
        R("POS.CashOverage", DefaultAccounts.CashOverage, "زيادة نقدية الصندوق", "Cash overage"),
        R("POS.CashShortage", DefaultAccounts.CashShortage, "عجز نقدية الصندوق", "Cash shortage"),
        R("POS.PettyCash", DefaultAccounts.PettyCashExpenses, "مصروفات نثرية", "Petty cash expenses"),
        R("HR.SalariesExpense", DefaultAccounts.SalariesExpense, "رواتب وأجور", "Salaries expense"),
        R("HR.SalariesPayable", DefaultAccounts.SalariesPayable, "رواتب مستحقة", "Salaries payable"),
        R("HR.SocialInsuranceExpense", DefaultAccounts.SocialInsuranceExpense, "التأمينات - حصة المنشأة", "Social insurance expense"),
        R("HR.SocialInsurancePayable", DefaultAccounts.SocialInsurancePayable, "تأمينات مستحقة", "Social insurance payable"),
        R("HR.EndOfServiceExpense", DefaultAccounts.EndOfServiceExpense, "مكافأة نهاية الخدمة", "End of service expense"),
        R("HR.EmployeeLoans", DefaultAccounts.EmployeeLoans, "سلف الموظفين", "Employee loans"),
        R("Manufacturing.AppliedCosts", DefaultAccounts.AppliedManufacturingCosts, "تكاليف تصنيع محمّلة", "Applied manufacturing costs"),
        R("CarShowroom.VehicleInventory", DefaultAccounts.VehicleInventory, "مخزون السيارات", "Vehicle inventory"),
        R("CarShowroom.VehiclesInTransit", DefaultAccounts.VehiclesInTransit, "سيارات في الطريق", "Vehicles in transit"),
        R("CarShowroom.ImportCostsClearing", DefaultAccounts.ImportCostsClearing, "تكاليف استيراد تحت التسوية", "Import costs clearing"),
        R("CarShowroom.SalesRevenue", DefaultAccounts.CarSalesRevenue, "إيرادات بيع السيارات", "Car sales revenue"),
        R("CarShowroom.CostOfCarsSold", DefaultAccounts.CarCogs, "تكلفة السيارات المباعة", "Cost of cars sold"),
        R("CarShowroom.AddOnRevenue", DefaultAccounts.CarAddOnRevenue, "إيرادات إضافات السيارات", "Car add-on revenue"),
        R("CarShowroom.AddOnCost", DefaultAccounts.CarAddOnCost, "تكلفة إضافات السيارات", "Car add-on cost"),
        R("CarShowroom.CustomerDeposits", DefaultAccounts.CustomerDeposits, "عربونات العملاء", "Customer deposits"),
        R("CarShowroom.ForfeitedDeposits", DefaultAccounts.ForfeitedDeposits, "عربونات مصادرة", "Forfeited deposits"),
        R("CarShowroom.AccruedCharges", DefaultAccounts.AccruedLandedCosts, "مستحقات جمارك وموانئ", "Accrued customs and port fees"),
        R("CarShowroom.VehicleExpenses", DefaultAccounts.VehicleExpenses, "مصاريف تشغيل السيارات", "Vehicle expenses"),
        R("CarShowroom.PurchaseCancellationLoss", DefaultAccounts.PurchaseCancellationLoss, "خسائر إلغاء المشتريات", "Purchase cancellation loss"),
        R("CarShowroom.CustomerRecharge", DefaultAccounts.CustomerRecharge, "مدفوعات نيابة عن العملاء", "Customer recharge"),
        R("CarShowroom.SupplierClaims", DefaultAccounts.SupplierClaims, "مطالبات على الموردين", "Supplier claims"),
        R("CarShowroom.TammWallet", DefaultAccounts.TammWallet, "رصيد محفظة تم", "Tamm wallet"),
    };

    private static readonly Dictionary<string, Definition> ByKey = All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>تعريف المفتاح؛ المفتاح غير المعروف للكود (أضافه محرر القوالب) يُربط بـ <see cref="AccountBinding"/>.</summary>
    public static Definition Of(string key)
        => ByKey.TryGetValue(key, out var d) ? d : new Definition(key, BindingStore, null, null, key, key);

    /// <summary>صيغة المفتاح: مقاطع لاتينية مفصولة بنقطة (Module.Purpose).</summary>
    public static bool IsValidFormat(string key)
        => key.Length is > 2 and <= 100 && key.Split('.').Length >= 2
           && key.Split('.').All(part => part.Length > 0 && char.IsLetter(part[0]) && part.All(char.IsLetterOrDigit));

    public static string Normalize(string? key) => string.IsNullOrWhiteSpace(key) ? string.Empty : ByKey.TryGetValue(key.Trim(), out var d) ? d.Key : key.Trim();
}
