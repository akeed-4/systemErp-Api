using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// حسابات الموقع (الموقع/المعرض) الخاصة: لكل موقع إضافي أربعة حسابات باسمه مرتبطة به، تحت أب حساب الدور المقابل في الإعدادات:
/// <list type="bullet">
/// <item>مخزون الموقع تحت المخزون (114)، ويُربط فقط إن لم يُحدَّد له حساب ولا حركة مخزون عليه (فلا ينقسم رصيده).</item>
/// <item>تكلفة إيراد الموقع تحت تكلفة المبيعات (51)، ومبيعاته تحت إيرادات المبيعات (41)، وصندوقه تحت النقدية والبنوك (111).</item>
/// </list>
/// الرمز = رمز الأب + رقم متسلسل من ثلاث خانات (114001، 41001...). الموقع الرئيسي يبقى على حسابات الإعدادات (عدا مخزونه 114001 منذ التأسيس).
/// </summary>
public static class SiteAccounts
{
    public enum Role { Inventory, Cogs, Sales, Cash, Shortage, Surplus }

    public static string Name(Role role, string? siteName, bool english = false) => role switch
    {
        Role.Inventory => WarehouseService.InventoryAccountName(siteName, english),
        Role.Cogs => english ? $"Cost of revenue - {siteName}" : $"تكلفة إيرادات - {siteName}",
        Role.Sales => english ? $"Sales - {siteName}" : $"مبيعات - {siteName}",
        Role.Shortage => english ? $"Inventory shortage - {siteName}" : $"عجز مخزون - {siteName}",
        Role.Surplus => english ? $"Inventory surplus - {siteName}" : $"زيادة مخزون - {siteName}",
        _ => english ? $"Cash box - {siteName}" : $"صندوق - {siteName}",
    };

    /// <summary>دور الحساب المرتبط بالموقع من رمزه في حقول الموقع (لإعادة تسميته مع اسم الموقع).</summary>
    public static Role? RoleOf(Warehouse w, string code)
        => code == w.InventoryAccountCode ? Role.Inventory : code == w.CogsAccountCode ? Role.Cogs
            : code == w.SalesAccountCode ? Role.Sales : code == w.CashAccountCode ? Role.Cash
            : code == w.ShortageAccountCode ? Role.Shortage : code == w.SurplusAccountCode ? Role.Surplus : null;

    /// <summary>ينقص الموقعَ حسابٌ من حساباته الخاصة (الرئيسي: تكلفة الإيراد والعجز والزيادة؛ مبيعاته وصندوقه على الإعدادات).</summary>
    public static bool IsMissing(Warehouse w)
        => w.CogsAccountCode == null || w.ShortageAccountCode == null || w.SurplusAccountCode == null
           || (!w.IsDefault && (w.InventoryAccountCode == null || w.SalesAccountCode == null || w.CashAccountCode == null));

    /// <summary>
    /// ينشئ ما ينقص من حسابات الموقع ويربطها به (لا يمس حساباً محدداً). <paramref name="isNew"/>: موقع جديد لم تُحفظ له حركات بعد.
    /// يُرجع عدد الحسابات المنشأة. الحفظ على المستدعي.
    /// </summary>
    public static async Task<int> EnsureAsync(ErpDbContext db, Warehouse w, bool isNew, CancellationToken ct)
    {
        if (w.Id == Guid.Empty) w.Id = Guid.NewGuid();
        var settings = await PostingAccounts.SettingsAsync(db, ct);
        var cash = TreasuryResolver.Resolve(PaymentMethod.Cash, await db.Set<PaymentMethodItem>().AsNoTracking().ToListAsync(ct));
        var created = 0;

        // المخزون: حساب خاص باسمه؛ والموقع القائم على حساب غيره (الرئيسي أو حساب الإعدادات) يُنشأ له حسابه ما دام بلا حركات
        if ((w.InventoryAccountCode == null || await SharesInventoryAsync(db, w, settings, ct)) && (isNew || !await HasStockAsync(db, w, ct)))
        {
            w.InventoryAccountCode = await CreateAsync(db, w, Role.Inventory,
                await db.Set<Account>().AnyAsync(a => a.Code == DefaultAccounts.InventoryGroup, ct) ? DefaultAccounts.InventoryGroup : await ParentOfAsync(db, settings.Inventory(), ct), ct);
            created++;
        }
        if (w.CogsAccountCode == null) { w.CogsAccountCode = await CreateAsync(db, w, Role.Cogs, await ParentOfAsync(db, settings.Cogs(), ct), ct); created++; }
        // عجز المخزون لكل موقع (حتى الرئيسي) تحت مجموعة حساب العجز في الإعدادات (513 ← 51 افتراضياً)،
        // وزيادته إيراد تحت الإيرادات الأخرى (42)
        if (w.ShortageAccountCode == null) { w.ShortageAccountCode = await CreateAsync(db, w, Role.Shortage, await ParentOfAsync(db, settings.InventoryShortage(), ct), ct); created++; }
        if (w.SurplusAccountCode == null)
        {
            await DefaultAccounts.EnsureAsync(db, ct, DefaultAccounts.OtherRevenue);
            w.SurplusAccountCode = await CreateAsync(db, w, Role.Surplus, DefaultAccounts.OtherRevenue, ct);
            created++;
        }
        // الرئيسي: مبيعاته وصندوقه على حسابات الإعدادات
        if (w.IsDefault) return created;
        if (w.SalesAccountCode == null) { w.SalesAccountCode = await CreateAsync(db, w, Role.Sales, await ParentOfAsync(db, settings.SalesRevenue(), ct), ct); created++; }
        if (w.CashAccountCode == null) { w.CashAccountCode = await CreateAsync(db, w, Role.Cash, await ParentOfAsync(db, cash, ct), ct); created++; }
        return created;
    }

    /// <summary>للمواقع القائمة (والرئيسي لحساباته الخاصة): ينشئ الناقص لكلٍ منها. يُرجع عدد المواقع التي أُنشئ لها حساب.</summary>
    public static async Task<int> EnsureAllAsync(ErpDbContext db, CancellationToken ct)
    {
        // كل المواقع (الأقدم أولاً): الناقص حسابه، أو القائم على حساب موقع آخر فيُنشأ له حسابه (بلا حركات مخزون)
        var sites = await db.Set<Warehouse>().OrderBy(w => w.CreatedAt).ToListAsync(ct);
        var touched = 0;
        foreach (var w in sites)
            if (await EnsureAsync(db, w, isNew: false, ct) > 0) touched++;
        return touched;
    }

    /// <summary>حساب مخزون الموقع ليس له: حساب المخزون العام، أو حساب موقع آخر (مرتبط به أو مستخدم له).</summary>
    private static async Task<bool> SharesInventoryAsync(ErpDbContext db, Warehouse w, AccountingSettings settings, CancellationToken ct)
    {
        if (w.InventoryAccountCode is not { } code) return false;
        if (code == settings.Inventory()) return true;
        return await db.Set<Account>().AnyAsync(a => a.Code == code && a.LinkedEntityType == LinkedEntityType.Warehouse && a.LinkedEntityId != w.Id, ct)
            || await db.Set<Warehouse>().AnyAsync(x => x.Id != w.Id && x.InventoryAccountCode == code && x.CreatedAt < w.CreatedAt, ct);
    }

    private static Task<bool> HasStockAsync(ErpDbContext db, Warehouse w, CancellationToken ct)
        => db.Set<StockMovement>().AnyAsync(m => m.WarehouseId == w.Id || (w.IsDefault && m.WarehouseId == null), ct);

    /// <summary>أب حساب الدور في الدليل (مثلاً 411 ← 41). الحساب بلا أب يُرفض: الموقع يحتاج مجموعة يُنشأ تحتها.</summary>
    private static async Task<string> ParentOfAsync(ErpDbContext db, string code, CancellationToken ct)
        => await db.Set<Account>().Where(a => a.Code == code).Select(a => a.ParentCode).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(string.Format(Messages.SiteAccountParentMissing, code));

    private static async Task<string> CreateAsync(ErpDbContext db, Warehouse w, Role role, string parentCode, CancellationToken ct)
    {
        var parent = await db.Set<Account>().FirstOrDefaultAsync(a => a.Code == parentCode, ct)
            ?? throw new ValidationFailedException(string.Format(Messages.SiteAccountParentMissing, parentCode));
        // التسلسل التالي تحت الأب (المحفوظ والمضاف في هذه المعاملة)
        var length = parentCode.Length + 3;
        var taken = (await db.Set<Account>().Where(a => a.Code.StartsWith(parentCode) && a.Code.Length == length).Select(a => a.Code).ToListAsync(ct))
            .Concat(db.ChangeTracker.Entries<Account>().Where(e => e.State == EntityState.Added).Select(e => e.Entity.Code))
            .ToHashSet();
        var seq = Enumerable.Range(1, 999).FirstOrDefault(n => !taken.Contains(parentCode + n.ToString("000")));
        if (seq == 0) throw new ConflictException(string.Format(Messages.SiteAccountsFull, parentCode));
        var code = parentCode + seq.ToString("000");
        var english = string.IsNullOrWhiteSpace(w.NameEn) ? w.NameAr : w.NameEn;
        parent.IsGroup = true;
        db.Add(new Account
        {
            Code = code, NameAr = Name(role, w.NameAr), NameEn = Name(role, english, english: true), Type = parent.Type, ParentCode = parent.Code,
            Level = parent.Level + 1, IsDebitNature = parent.IsDebitNature, Currency = parent.Currency,
            LinkedEntityType = LinkedEntityType.Warehouse, LinkedEntityId = w.Id,
        });
        return code;
    }
}
