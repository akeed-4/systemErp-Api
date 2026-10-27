using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الحسابات الفعّالة للترحيل: إعداد المنشأة (AccountingSettings) إن حُدِّد وإلا الحساب الافتراضي في الشجرة.
/// الخدمات تقرأ الحساب من هنا بدل DefaultAccounts، والحساب المحدد في المستند يتقدم على الناتج.
/// </summary>
internal static class PostingAccounts
{
    /// <param name="LockedAfterUse">
    /// لا يُغيَّر بعد أي حركة عليه ولو صار رصيده صفراً: تعديل مستند سابق يُعيد ترحيله بالحساب الحالي فيعكس القديم من حساب ويُرحِّل
    /// الجديد على آخر (رصيد مخزون موزَّع خطأً)، وحركات الضريبة تسقط من إقرار فترتها.
    /// </param>
    public sealed record Role(string Key, Func<AccountingSettings, string?> Get, Action<AccountingSettings, string?> Set,
        string? Default, AccountCategory[] Types, bool LockedAfterUse = false, bool RequiredForPeriodic = false)
    {
        public string? Effective(AccountingSettings s) => Get(s) ?? Default;
        public string Label => Messages.ResourceManager.GetString("AccountRole_" + Key, Messages.Culture) ?? Key;
    }

    private static readonly AccountCategory[] Asset = { AccountCategory.Asset };
    private static readonly AccountCategory[] Expense = { AccountCategory.Expense };
    private static readonly AccountCategory[] Revenue = { AccountCategory.Revenue };
    private static readonly AccountCategory[] ProfitOrLoss = { AccountCategory.Revenue, AccountCategory.Expense };

    public static readonly Role[] Roles =
    {
        new(nameof(AccountingSettings.InventoryAccountCode), s => s.InventoryAccountCode, (s, v) => s.InventoryAccountCode = v,
            DefaultAccounts.Inventory, Asset, LockedAfterUse: true),
        new(nameof(AccountingSettings.CogsAccountCode), s => s.CogsAccountCode, (s, v) => s.CogsAccountCode = v, DefaultAccounts.Cogs, Expense),
        new(nameof(AccountingSettings.InventoryShortageAccountCode), s => s.InventoryShortageAccountCode, (s, v) => s.InventoryShortageAccountCode = v,
            DefaultAccounts.InventoryAdjustment, Expense),
        new(nameof(AccountingSettings.InventorySurplusAccountCode), s => s.InventorySurplusAccountCode, (s, v) => s.InventorySurplusAccountCode = v,
            DefaultAccounts.InventoryAdjustment, ProfitOrLoss),
        new(nameof(AccountingSettings.PurchasesAccountCode), s => s.PurchasesAccountCode, (s, v) => s.PurchasesAccountCode = v,
            null, Expense, RequiredForPeriodic: true),
        new(nameof(AccountingSettings.PurchaseReturnsAccountCode), s => s.PurchaseReturnsAccountCode, (s, v) => s.PurchaseReturnsAccountCode = v,
            null, ProfitOrLoss),
        new(nameof(AccountingSettings.OpeningInventoryAccountCode), s => s.OpeningInventoryAccountCode, (s, v) => s.OpeningInventoryAccountCode = v,
            null, Expense, RequiredForPeriodic: true),
        new(nameof(AccountingSettings.EndingInventoryAccountCode), s => s.EndingInventoryAccountCode, (s, v) => s.EndingInventoryAccountCode = v,
            null, ProfitOrLoss, RequiredForPeriodic: true),
        new(nameof(AccountingSettings.SalesRevenueAccountCode), s => s.SalesRevenueAccountCode, (s, v) => s.SalesRevenueAccountCode = v,
            DefaultAccounts.Revenue, Revenue),
        new(nameof(AccountingSettings.SalesReturnsAccountCode), s => s.SalesReturnsAccountCode, (s, v) => s.SalesReturnsAccountCode = v,
            null, ProfitOrLoss),
        new(nameof(AccountingSettings.OutputVatAccountCode), s => s.OutputVatAccountCode, (s, v) => s.OutputVatAccountCode = v,
            DefaultAccounts.OutputVat, new[] { AccountCategory.Liability }, LockedAfterUse: true),
        new(nameof(AccountingSettings.InputVatAccountCode), s => s.InputVatAccountCode, (s, v) => s.InputVatAccountCode = v,
            DefaultAccounts.InputVat, new[] { AccountCategory.Asset, AccountCategory.Liability }, LockedAfterUse: true),
    };

    public static async Task<AccountingSettings> SettingsAsync(ErpDbContext db, CancellationToken ct)
        => await db.Set<AccountingSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new AccountingSettings();

    public static Role InventoryRole => Roles.First(r => r.Key == nameof(AccountingSettings.InventoryAccountCode));

    /// <summary>
    /// حساب مخزون المستودع، وإلا حساب المخزون في الإعدادات. بلا مستودع: الافتراضي، كما تُحسب الحركات القديمة بلا مستودع.
    /// (الحساب اليدوي في المستند يتقدم على الناتج عند المستدعي.)
    /// </summary>
    public static async Task<string> InventoryAsync(this AccountingSettings s, ErpDbContext db, Guid? warehouseId, CancellationToken ct)
    {
        var warehouses = db.Set<Warehouse>().AsNoTracking();
        var own = warehouseId.HasValue
            ? await warehouses.Where(w => w.Id == warehouseId).Select(w => w.InventoryAccountCode).FirstOrDefaultAsync(ct)
            : await warehouses.OrderByDescending(w => w.IsDefault).ThenBy(w => w.CreatedAt).Select(w => w.InventoryAccountCode).FirstOrDefaultAsync(ct);
        return own ?? s.Inventory();
    }

    /// <summary>حسابات مخزون المستودعات المحددة صراحةً.</summary>
    public static async Task<HashSet<string>> WarehouseInventoryAccountsAsync(ErpDbContext db, CancellationToken ct)
        => (await db.Set<Warehouse>().AsNoTracking().Where(w => w.InventoryAccountCode != null).Select(w => w.InventoryAccountCode!).Distinct().ToListAsync(ct))
            .ToHashSet();

    public static string Inventory(this AccountingSettings s) => s.InventoryAccountCode ?? DefaultAccounts.Inventory;
    public static string Cogs(this AccountingSettings s) => s.CogsAccountCode ?? DefaultAccounts.Cogs;
    public static string InventoryShortage(this AccountingSettings s) => s.InventoryShortageAccountCode ?? DefaultAccounts.InventoryAdjustment;
    public static string InventorySurplus(this AccountingSettings s) => s.InventorySurplusAccountCode ?? DefaultAccounts.InventoryAdjustment;
    public static string SalesRevenue(this AccountingSettings s) => s.SalesRevenueAccountCode ?? DefaultAccounts.Revenue;
    /// <summary>حساب الإيراد الافتراضي لسطر المستند: مرتجع المبيعات على حسابه إن حُدِّد، وإلا حساب الإيراد نفسه.</summary>
    public static string SalesRevenue(this AccountingSettings s, bool isReturn)
        => (isReturn ? s.SalesReturnsAccountCode : null) ?? s.SalesRevenue();
    public static string Purchases(this AccountingSettings s) => Required(s.PurchasesAccountCode, nameof(AccountingSettings.PurchasesAccountCode));
    /// <summary>مردودات المشتريات على حسابها إن حُدِّد، وإلا تُخفَّض المشتريات نفسها.</summary>
    public static string PurchaseReturns(this AccountingSettings s) => s.PurchaseReturnsAccountCode ?? s.Purchases();
    public static string OpeningInventory(this AccountingSettings s) => Required(s.OpeningInventoryAccountCode, nameof(AccountingSettings.OpeningInventoryAccountCode));
    public static string EndingInventory(this AccountingSettings s) => Required(s.EndingInventoryAccountCode, nameof(AccountingSettings.EndingInventoryAccountCode));
    public static string OutputVat(this AccountingSettings s) => s.OutputVatAccountCode ?? DefaultAccounts.OutputVat;
    public static string InputVat(this AccountingSettings s) => s.InputVatAccountCode ?? DefaultAccounts.InputVat;

    /// <summary>حسابات الدوري لا افتراضي لها: مستند بتاريخ دوري سابق بعد مسح حسابه يُرفض برسالة الإعداد الناقص.</summary>
    private static string Required(string? code, string key)
        => code ?? throw new ValidationFailedException(string.Format(Messages.AccountSettingRequiredForPeriodic, Roles.First(r => r.Key == key).Label));

    /// <summary>طريقة الجرد السارية في تاريخ المستند.</summary>
    public static InventorySystem InventoryMethodOn(this AccountingSettings s, DateTime date)
        => s.InventoryMethodEffectiveFrom is { } from && date.Date < from ? s.PreviousInventoryMethod ?? s.InventoryMethod : s.InventoryMethod;
}
