using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERP.Service.Services.Shared;

public class WarehouseService : CrudService<Warehouse, WarehouseDto, CreateWarehouseDto, UpdateWarehouseDto>, IWarehouseService
{
    public WarehouseService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelWarehouse;

    protected override IQueryable<Warehouse> ApplySearch(IQueryable<Warehouse> q, string t)
        => q.Where(w => w.Code.Contains(t) || w.NameAr.Contains(t) || w.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreateWarehouseDto d, Warehouse? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (await Db.Set<Warehouse>().AnyAsync(w => w.Code == d.Code && (existing == null || w.Id != existing.Id), ct))
            throw new ConflictException(Messages.WarehouseCodeInUse);
        if (existing is { IsDefault: true } && !d.IsDefault)
            throw new ConflictException(Messages.SetAnotherDefaultWarehouse);
        // حد الاشتراك: المستودعات الفعّالة لا تتجاوز عدد الفروع (عند الإضافة أو إعادة تفعيل مستودع موقوف)
        if (d.Status == "active" && existing?.Status != "active")
        {
            var sub = await Db.Set<Subscription>().OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
            if (sub != null && await Db.Set<Warehouse>().CountAsync(w => w.Status == "active", ct) >= sub.MaxBranches)
                throw new ConflictException(Messages.WarehouseLimitReached);
        }
        await ValidateInventoryAccountAsync(d, existing, ct);
    }

    /// <summary>
    /// حساب مخزون المستودع (تحديث جزئي: غير المرسَل يبقى، و"" يعيده لحساب الإعدادات): يصلح لدور المخزون ولا يقع على حساب ضريبة،
    /// ولا يتغيّر بعد أول حركة مخزون في المستودع، فتعديل مستند سابق يُرحَّل على الحساب الذي رُحِّل عليه أول مرة.
    /// </summary>
    private async Task ValidateInventoryAccountAsync(CreateWarehouseDto d, Warehouse? existing, CancellationToken ct)
    {
        d.InventoryAccountCode = d.InventoryAccountCode == null ? existing?.InventoryAccountCode
            : string.IsNullOrWhiteSpace(d.InventoryAccountCode) ? null : d.InventoryAccountCode.Trim();
        var role = PostingAccounts.InventoryRole;
        if (d.InventoryAccountCode is { } code)
        {
            var errors = await AccountRoleRules.ProblemsAsync(Db, new[] { (role, code) }, ct);
            var settings = await PostingAccounts.SettingsAsync(Db, ct);
            if (code == settings.InputVat() || code == settings.OutputVat())
                errors.Add(string.Format(Messages.AccountSettingSharedByRoles, code, role.Label));
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        }
        if (existing != null && d.InventoryAccountCode != existing.InventoryAccountCode
            && await Db.Set<StockMovement>().AnyAsync(m => m.WarehouseId == existing.Id || (existing.IsDefault && m.WarehouseId == null), ct))
            throw new ConflictException(string.Format(Messages.WarehouseInventoryAccountLocked, existing.NameAr));
    }

    protected override async Task OnCreatingAsync(Warehouse e, CreateWarehouseDto d, CancellationToken ct)
    {
        var any = await Db.Set<Warehouse>().AnyAsync(ct);
        if (!any) e.IsDefault = true; // أول مستودع هو الافتراضي
        if (e.IsDefault) await ClearOtherDefaultsAsync(e.Id, ct);
        // كل موقع: مخزونه وتكلفة إيراده وعجز وزيادة مخزونه باسمه؛ والإضافي مبيعاته وصندوقه أيضاً (حساب مخزون اختاره المستخدم يبقى)
        await SiteAccounts.EnsureAsync(Db, e, isNew: true, ct);
    }

    protected override async Task OnUpdatingAsync(Warehouse e, UpdateWarehouseDto d, CancellationToken ct)
    {
        if (e.IsDefault) await ClearOtherDefaultsAsync(e.Id, ct);
        // أسماء حسابات الموقع المرتبطة تتبع اسمه (كلٌ بدوره)
        var english = string.IsNullOrWhiteSpace(e.NameEn) ? e.NameAr : e.NameEn;
        foreach (var account in await Db.Set<Account>().Where(a => a.LinkedEntityType == LinkedEntityType.Warehouse && a.LinkedEntityId == e.Id).ToListAsync(ct))
        {
            var role = SiteAccounts.RoleOf(e, account.Code) ?? SiteAccounts.Role.Inventory;
            account.NameAr = SiteAccounts.Name(role, e.NameAr);
            account.NameEn = SiteAccounts.Name(role, english, english: true);
        }
    }

    /// <summary>ينشئ حسابات المواقع الناقصة للمواقع القائمة غير الرئيسية (المخزون فقط لموقع بلا حركات). يُرجع عدد المواقع المعدَّلة.</summary>
    public async Task<int> EnsureSiteAccountsAsync(CancellationToken ct = default)
    {
        var touched = await SiteAccounts.EnsureAllAsync(Db, ct);
        await Db.SaveChangesAsync(ct);
        return touched;
    }

    internal static string InventoryAccountName(string? name, bool english = false)
        => english ? $"Inventory - {name}" : $"مخزون - {name}";

    private async Task ClearOtherDefaultsAsync(Guid keep, CancellationToken ct)
    {
        foreach (var w in await Db.Set<Warehouse>().Where(w => w.IsDefault && w.Id != keep).ToListAsync(ct)) w.IsDefault = false;
    }

    /// <summary>
    /// حذف المستودع بلا أي أثر: لا حركة مخزون، ولا قيد على حساباته الخاصة، ولا مستند أو إعداد يشير إليه (فحص عام لكل عمود
    /// «...WarehouseId» في قاعدة البيانات، فيشمل أي مستند يُضاف لاحقاً)، ولا مستخدم أو دور هو موقعه الوحيد (وإلا صار وصوله لكل المواقع).
    /// المستوفي يُحذف مع حساباته الخاصة وأرصدته الصفرية وتسلسلات ترقيمه، ويُزال من مواقع المستخدمين ونطاقات الصلاحيات.
    /// </summary>
    protected override async Task OnDeletingAsync(Warehouse e, CancellationToken ct)
    {
        if (e.IsDefault) throw new ConflictException(Messages.CannotDeleteDefaultWarehouse);
        if (await Db.Set<StockMovement>().AnyAsync(m => m.WarehouseId == e.Id, ct))
            throw new ConflictException(Messages.WarehouseHasMovements);

        // الحركات المالية: قيود على حسابات المستودع الخاصة (المخزون، تكلفة الإيراد، المبيعات، الصندوق...)
        var accounts = await Db.Set<Account>().Where(a => a.LinkedEntityType == LinkedEntityType.Warehouse && a.LinkedEntityId == e.Id).ToListAsync(ct);
        var codes = accounts.Select(a => a.Code).ToList();
        if (codes.Count > 0 && await Db.Set<JournalEntryLine>().AnyAsync(l => codes.Contains(l.AccountCode), ct))
            throw new ConflictException(Messages.WarehouseHasFinancialMovements);

        // المستندات والإعدادات المشيرة إليه
        var usage = await UsageAsync(e.Id, ct);
        if (usage.Count > 0) throw new ConflictException(string.Format(Messages.WarehouseInUse, string.Join("، ", usage)));

        // المستخدمون والأدوار: الموقع الوحيد لا يُزال (القائمة الفارغة تعني كل المواقع)
        var id = e.Id.ToString();
        var users = await Db.Set<User>().Where(u => u.AllowedWarehouseIds != null && u.AllowedWarehouseIds.Contains(id)).ToListAsync(ct);
        var scopes = await Db.Set<UserRolePermission>().Where(p => p.ScopeWarehouseIds != null && p.ScopeWarehouseIds.Contains(id)).ToListAsync(ct);
        var only = users.Where(u => WithoutSite(u.AllowedWarehouseIds, e.Id) == null).Select(u => u.Name).ToList();
        if (only.Count > 0) throw new ConflictException(string.Format(Messages.WarehouseOnlySiteOfUsers, string.Join("، ", only)));
        if (scopes.Any(p => WithoutSite(p.ScopeWarehouseIds, e.Id) == null)) throw new ConflictException(Messages.WarehouseOnlySiteOfRoleScope);
        foreach (var u in users) u.AllowedWarehouseIds = WithoutSite(u.AllowedWarehouseIds, e.Id);
        foreach (var p in scopes) p.ScopeWarehouseIds = WithoutSite(p.ScopeWarehouseIds, e.Id);

        // ما يخص المستودع وحده: حساباته (بلا قيود)، أرصدته الصفرية، وتسلسلات ترقيمه
        Db.RemoveRange(accounts);
        Db.RemoveRange(await Db.Set<WarehouseStock>().Where(s => s.WarehouseId == e.Id).ToListAsync(ct));
        Db.RemoveRange(await Db.Set<BatchStock>().Where(s => s.WarehouseId == e.Id).ToListAsync(ct));
        var sequenceKey = $"@warehouse:{e.Id}";
        Db.RemoveRange(await Db.Set<NumberSequence>().Where(s => s.Key.Contains(sequenceKey)).ToListAsync(ct));
    }

    /// <summary>القائمة بعد إزالة الموقع، أو null إن لم يبقَ فيها موقع.</summary>
    private static string? WithoutSite(string? csv, Guid site)
    {
        var rest = (WarehouseSessionService.ParseIds(csv) ?? new List<Guid>()).Where(g => g != site).ToList();
        return rest.Count == 0 ? null : string.Join(",", rest);
    }

    /// <summary>تسمية الجداول المشيرة للمستودع في رسالة المنع (الاسم التقني لما لا تسمية له).</summary>
    private static readonly Dictionary<string, string> UsageLabels = new()
    {
        [nameof(InvoiceBase)] = "فواتير ومرتجعات", [nameof(JournalEntry)] = "قيود يومية", [nameof(Voucher)] = "سندات قبض وصرف",
        [nameof(DeliveryNote)] = "بيانات تسليم واستلام", [nameof(Quotation)] = "عروض أسعار", [nameof(CommercialOrder)] = "أوامر بيع وشراء",
        [nameof(MaterialRequisition)] = "طلبات شراء", [nameof(PurchaseRfq)] = "طلبات عروض أسعار", [nameof(StockTransfer)] = "تحويلات مخزنية",
        [nameof(InventoryCount)] = "جرد", [nameof(ProductionOrder)] = "أوامر تصنيع", [nameof(StockReservation)] = "حجوزات مخزون",
        [nameof(ProjectMaterialIssue)] = "أذونات صرف مواد", [nameof(CommercialContract)] = "عقود ومشاريع", [nameof(FixedAsset)] = "أصول ثابتة",
        [nameof(FixedAssetDepreciation)] = "إهلاك أصول", [nameof(Equipment)] = "معدات", [nameof(PosShift)] = "ورديات نقاط البيع",
        [nameof(PosTerminal)] = "أجهزة نقاط البيع", [nameof(PosInvoiceSettings)] = "إعدادات نقاط البيع", [nameof(Vehicle)] = "مركبات",
        [nameof(CarSalesContract)] = "عقود بيع سيارات", [nameof(CarProcurementOrder)] = "طلبات شراء سيارات", [nameof(WorkshopSettings)] = "إعدادات الورشة",
    };

    /// <summary>
    /// كل جدول فيه عمود ينتهي بـ WarehouseId (عدا المستودع وأرصدته المؤقتة) ويشير لهذا المستودع، باستعلام واحد.
    /// الفحص من نموذج قاعدة البيانات نفسه فلا يُنسى مستند جديد. المعرّفات فريدة فلا حاجة لفلتر المنشأة.
    /// </summary>
    private async Task<List<string>> UsageAsync(Guid warehouseId, CancellationToken ct)
    {
        var skip = new HashSet<Type> { typeof(Warehouse), typeof(WarehouseStock), typeof(BatchStock), typeof(StockMovement) };
        var seen = new HashSet<string>();
        var parts = new List<string>();
        foreach (var et in Db.Model.GetEntityTypes())
        {
            if (skip.Contains(et.ClrType) || et.IsOwned() || et.GetTableName() is not { } table) continue;
            var store = StoreObjectIdentifier.Table(table, et.GetSchema());
            var root = et.GetRootType().ClrType.Name;
            var label = UsageLabels.GetValueOrDefault(root) ?? root;
            foreach (var p in et.GetProperties().Where(p => p.Name.EndsWith("WarehouseId", StringComparison.Ordinal)
                         && (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?))))
            {
                if (p.GetColumnName(store) is not { } column || !seen.Add($"{et.GetSchema()}.{table}.{column}")) continue;
                var qualified = et.GetSchema() is { } schema ? $"[{schema}].[{table}]" : $"[{table}]";
                parts.Add($"SELECT N'{label.Replace("'", "''")}' AS [Value] WHERE EXISTS (SELECT 1 FROM {qualified} WHERE [{column}] = @warehouseId)");
            }
        }
        if (parts.Count == 0) return new();
        var found = await Db.Database.SqlQueryRaw<string>(string.Join(" UNION ALL ", parts),
            new Microsoft.Data.SqlClient.SqlParameter("@warehouseId", warehouseId)).ToListAsync(ct);
        return found.Distinct().ToList();
    }
}
