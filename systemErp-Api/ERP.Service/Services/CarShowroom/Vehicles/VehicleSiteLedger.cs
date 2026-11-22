using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// مخزون السيارات في حساب موقعها. كل مسارات التكلفة القائمة (الشراء، أمر الاستيراد، التكاليف المحمّلة، الاستلام، الإلغاء، الجرد) تبقى على
/// مخزون السيارات العام (1142) كحساب تجميع، ثم تُزامَن كل سيارة:
/// <list type="bullet">
/// <item>المتاحة أو المحجوزة في موقع له حساباته الخاصة: تكلفتها الكاملة في مخزون الموقع (مدين الموقع / دائن 1142 بالفرق).</item>
/// <item>غيرها (مسودة، في الطريق، ملغاة، مشطوبة): لا شيء في الموقع (ما حُجز يعود لـ 1142).</item>
/// <item>المباعة لا تُزامَن: البيع يخصمها من حساب موقعها مباشرة (<see cref="TakeForSaleAsync"/>)، ومرتجعه يعيدها إليه.</item>
/// </list>
/// القيد مجمَّع لكل استدعاء بمصدر <c>car_site_inventory</c>. الموقع الرئيسي بلا حسابات خاصة فتبقى سياراته في 1142.
/// </summary>
internal static class VehicleSiteLedger
{
    public const string SourceType = "car_site_inventory";

    /// <summary>حساب مخزون كل موقع له حساباته الخاصة (المبيعات والمخزون).</summary>
    public static Task<Dictionary<Guid, string>> SiteAccountsAsync(ErpDbContext db, CancellationToken ct)
        => db.Set<Warehouse>().AsNoTracking().Where(w => w.SalesAccountCode != null && w.InventoryAccountCode != null)
            .ToDictionaryAsync(w => w.Id, w => w.InventoryAccountCode!, ct);

    /// <summary>يزامن السيارات المحددة مع حساب موقعها ويرحّل الفرق في قيد واحد. يحفظ التغييرات.</summary>
    public static async Task SyncAsync(ErpDbContext db, IAccountingPostingService posting, IEnumerable<Guid> vehicleIds, string reason, CancellationToken ct)
    {
        var ids = vehicleIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var vehicles = await db.Set<Vehicle>().Where(v => ids.Contains(v.Id) && v.Status != VehicleStatus.Sold).ToListAsync(ct);
        if (vehicles.Count == 0) return;
        var sites = await SiteAccountsAsync(db, ct);
        var pool = (await SalesCharges.SettingsAsync(db, ct)).VehicleInventoryAccount;
        var net = new Dictionary<string, decimal>();
        void Move(string account, decimal amount) => net[account] = net.GetValueOrDefault(account) + amount;

        foreach (var v in vehicles)
        {
            var inStock = InventoryCountLines.InStock.Contains(v.Status);
            var target = inStock && v.WarehouseId is Guid w && sites.TryGetValue(w, out var site) ? site : null;
            var amount = target == null ? 0m : DocumentPricing.Round(v.TotalCost);
            if (target == v.SiteInventoryAccountCode && amount == v.SiteBookedCost) continue;
            // ما حُجز سابقاً يعود للتجميع، ثم يُحجز الجديد في موقعه
            if (v.SiteInventoryAccountCode != null && v.SiteBookedCost != 0) { Move(v.SiteInventoryAccountCode, -v.SiteBookedCost); Move(pool, v.SiteBookedCost); }
            if (target != null && amount != 0) { Move(target, amount); Move(pool, -amount); }
            v.SiteInventoryAccountCode = target; v.SiteBookedCost = amount;
        }
        await PostAsync(db, posting, net, reason, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// البيع: تُزامَن سيارات الفاتورة ثم تخرج من حساب موقعها؛ يُرجع حساب المخزون الذي تُخصم منه (حساب الموقع إن كانت كلها فيه، وإلا
    /// <paramref name="fallback"/> أو 1142). الحجز يُصفَّر لأن قيد البيع يخصمه.
    /// </summary>
    public static async Task<string?> TakeForSaleAsync(ErpDbContext db, IAccountingPostingService posting, IReadOnlyCollection<Guid> vehicleIds, string? fallback,
        CancellationToken ct)
    {
        await SyncAsync(db, posting, vehicleIds, "نقل السيارات لمخزون معرضها قبل البيع", ct);
        var vehicles = await db.Set<Vehicle>().Where(v => vehicleIds.Contains(v.Id)).ToListAsync(ct);
        var accounts = vehicles.Select(v => v.SiteInventoryAccountCode).Distinct().ToList();
        if (accounts.Count != 1 || accounts[0] is not string site)
        {
            // سيارات من مواقع مختلفة الحساب في فاتورة واحدة (لا يحدث: الفاتورة من معرض واحد): تعود للتجميع وتُخصم منه
            await UnbookAsync(db, posting, vehicles, "إعادة سيارات لمخزون السيارات العام قبل بيعها", ct);
            return fallback;
        }
        foreach (var v in vehicles) { v.SiteInventoryAccountCode = null; v.SiteBookedCost = 0; }
        return site;
    }

    /// <summary>عودة سيارات إلى المخزون بقيد خصمها/أعاد إليها حساب <paramref name="account"/> (إلغاء البيع أو مرتجعه): يُسجَّل حجزها بلا قيد.</summary>
    public static async Task RestoreAsync(ErpDbContext db, IEnumerable<Vehicle> vehicles, string? account, CancellationToken ct)
    {
        var sites = await SiteAccountsAsync(db, ct);
        if (account == null || !sites.ContainsValue(account)) return;
        foreach (var v in vehicles) { v.SiteInventoryAccountCode = account; v.SiteBookedCost = DocumentPricing.Round(v.TotalCost); }
    }

    /// <summary>عودة سيارات مباعة (إلغاء مرتجع البيع): قيد البيع المستعاد يخصمها من حسابها، فيُصفَّر الحجز بلا قيد.</summary>
    public static void Release(IEnumerable<Vehicle> vehicles)
    {
        foreach (var v in vehicles) { v.SiteInventoryAccountCode = null; v.SiteBookedCost = 0; }
    }

    /// <summary>يعيد ما حُجز للسيارات في مواقعها إلى مخزون السيارات العام (قبل إلغاء شرائها أو حذفها). الحفظ على المستدعي.</summary>
    public static async Task UnbookAsync(ErpDbContext db, IAccountingPostingService posting, List<Vehicle> vehicles, string reason, CancellationToken ct)
    {
        var pool = (await SalesCharges.SettingsAsync(db, ct)).VehicleInventoryAccount;
        var net = new Dictionary<string, decimal>();
        foreach (var v in vehicles.Where(v => v.SiteInventoryAccountCode != null && v.SiteBookedCost != 0))
        {
            net[v.SiteInventoryAccountCode!] = net.GetValueOrDefault(v.SiteInventoryAccountCode!) - v.SiteBookedCost;
            net[pool] = net.GetValueOrDefault(pool) + v.SiteBookedCost;
            v.SiteInventoryAccountCode = null; v.SiteBookedCost = 0;
        }
        await PostAsync(db, posting, net, reason, ct);
    }

    private static async Task PostAsync(ErpDbContext db, IAccountingPostingService posting, Dictionary<string, decimal> net, string reason, CancellationToken ct)
    {
        var lines = net.Where(x => x.Value != 0)
            .Select(x => x.Value > 0 ? new PostingLine(x.Key, x.Value, 0, reason) : new PostingLine(x.Key, 0, -x.Value, reason)).ToList();
        if (lines.Count == 0) return;
        await posting.PostAsync(new GenericPostingRequest
        {
            Date = SaudiTime.Today, Description = reason, SourceType = SourceType, Module = PlatformModules.CarShowroom, Lines = lines,
        }, ct);
    }
}
