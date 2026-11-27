using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly ErpDbContext _db;
    private readonly ErpDbContext? _catalog;

    public TenantProvisioningService(ErpDbContext db) => _db = db;

    /// <summary>قاعدة مستقلة: سياقها لا يعرف القاعدة المشتركة، فتُمرَّر ليُقرأ منها مصدر القوالب المركزي.</summary>
    internal TenantProvisioningService(ErpDbContext db, ErpDbContext catalog)
    {
        _db = db;
        _catalog = catalog;
    }

    private ErpDbContext Catalog => _catalog ?? _db.Catalog;

    public Task SeedAsync(Guid tenantId, string baseCurrencyCode, CancellationToken ct = default)
        => SeedAsync(tenantId, baseCurrencyCode, null, null, ct);

    public async Task SeedAsync(Guid tenantId, string baseCurrencyCode, string? activityKey, IEnumerable<string>? modules, CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction != null) { await SeedCoreAsync(tenantId, baseCurrencyCode, activityKey, modules, ct); return; }
        // القاعدة المستقلة تُزرع بلا معاملة مستدعٍ: معاملة هنا فلا يبقى دليل ناقص (إعادة التجهيز تبدأ من قاعدة نظيفة)
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await SeedCoreAsync(tenantId, baseCurrencyCode, activityKey, modules, ct);
        await tx.CommitAsync(ct);
    }

    private async Task SeedCoreAsync(Guid tenantId, string baseCurrencyCode, string? activityKey, IEnumerable<string>? modules, CancellationToken ct)
    {
        // الدليل: الأساسي + النشاط + الموديولات (بلا نشاط = التركيبة القديمة الكاملة)، ثم التحقق من اكتماله قبل أي بيانات أخرى
        var legacy = string.IsNullOrWhiteSpace(activityKey);
        var licensed = legacy ? ChartTemplateCatalog.LegacyModules : (modules ?? Array.Empty<string>()).ToArray();
        var all = await ChartTemplateCatalog.LoadAsync(Catalog, ct);
        var templates = await ChartTemplateCatalog.SelectAsync(Catalog, all, includeBase: true, legacy ? BusinessActivities.Trading : activityKey,
            licensed, ct);
        var report = await ChartProvisioner.ApplyAsync(_db, new ChartProvisioner.Request
        {
            Templates = templates, ActivityKey = legacy ? null : activityKey, SetActivity = true, Modules = licensed, Currency = baseCurrencyCode,
        }, ct);
        if (!report.Complete)
            throw new ConflictException(Messages.ChartSetupIncomplete + " " + string.Join(" | ", report.Errors.Concat(report.Items
                .Where(i => i.Status == ChartSetupItemStatus.Problem && i.Message != null).Select(i => i.Message!)).Take(5)));

        var chart = await _db.Set<Account>().AsNoTracking()
            .Where(a => a.Code == DefaultAccounts.InventoryGroup || a.Code == DefaultAccounts.Cogs || a.Code == DefaultAccounts.OtherRevenue)
            .ToDictionaryAsync(a => a.Code, ct);
        var costGroup = chart[DefaultAccounts.Cogs].ParentCode!;
        var costGroupLevel = await _db.Set<Account>().Where(a => a.Code == costGroup).Select(a => a.Level).FirstAsync(ct);
        int LevelOf(string code) => code == costGroup ? costGroupLevel : chart[code].Level;

        _db.Add(new Currency
        {
            TenantId = tenantId,
            Code = baseCurrencyCode, NameAr = "ريال سعودي", NameEn = "Saudi Riyal", Symbol = "ر.س",
            IsBaseCurrency = true,
        });

        // الفرع الرئيسي (الافتراضي): كل موقع يتبع فرعاً من النشاط نفسه
        var branch = new Branch
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            Code = Branches.DefaultCode, NameAr = Branches.DefaultNameAr, NameEn = Branches.DefaultNameEn, IsDefault = true,
        };
        _db.Add(branch);

        // الموقع الرئيسي بحساب مخزونه الخاص تحت 114 (كل موقع يُضاف لاحقاً يُنشأ له حسابه بالترقيم نفسه)
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branch.Id,
            Code = "WH-001", NameAr = "الموقع الرئيسي", NameEn = "Main Warehouse", IsDefault = true,
        };
        var inventoryGroup = chart[DefaultAccounts.InventoryGroup];
        var siteAccount = new Account
        {
            TenantId = tenantId,
            Code = DefaultAccounts.InventoryGroup + "001",
            NameAr = WarehouseService.InventoryAccountName(warehouse.NameAr), NameEn = WarehouseService.InventoryAccountName(warehouse.NameEn, english: true),
            Type = inventoryGroup.Type, ParentCode = inventoryGroup.Code, Level = inventoryGroup.Level + 1,
            IsDebitNature = true, Currency = baseCurrencyCode,
            LinkedEntityType = LinkedEntityType.Warehouse, LinkedEntityId = warehouse.Id,
        };
        _db.Add(siteAccount);
        warehouse.InventoryAccountCode = siteAccount.Code;

        // تكلفة إيراد الموقع الرئيسي وعجز مخزونه تحت مجموعة تكلفة المبيعات (أب 511 و513)، وزيادة مخزونه إيراد تحت الإيرادات الأخرى (42)
        Account SiteAccount(string code, SiteAccounts.Role role, string parent, AccountCategory type) => new()
        {
            TenantId = tenantId, Code = code,
            NameAr = SiteAccounts.Name(role, warehouse.NameAr), NameEn = SiteAccounts.Name(role, warehouse.NameEn, english: true),
            Type = type, ParentCode = parent, Level = LevelOf(parent) + 1,
            IsDebitNature = DefaultAccounts.IsDebitNature(code, type), Currency = baseCurrencyCode,
            LinkedEntityType = LinkedEntityType.Warehouse, LinkedEntityId = warehouse.Id,
        };
        var cogs = SiteAccount(costGroup + "001", SiteAccounts.Role.Cogs, costGroup, AccountCategory.Expense);
        var shortage = SiteAccount(costGroup + "002", SiteAccounts.Role.Shortage, costGroup, AccountCategory.Expense);
        var surplus = SiteAccount(DefaultAccounts.OtherRevenue + "001", SiteAccounts.Role.Surplus, DefaultAccounts.OtherRevenue, AccountCategory.Revenue);
        _db.AddRange(cogs, shortage, surplus);
        (warehouse.CogsAccountCode, warehouse.ShortageAccountCode, warehouse.SurplusAccountCode) = (cogs.Code, shortage.Code, surplus.Code);
        _db.Add(warehouse);

        _db.Add(new CostingPolicy
        {
            TenantId = tenantId,
            Method = CostingMethod.MovingAverage,
            StandardCostVarianceAccountCode = DefaultAccounts.InventoryAdjustment,
        });

        _db.Add(new PosInvoiceSettings { TenantId = tenantId });

        foreach (var type in CarShowroom.ExpenseTypeSeed.Build())
        {
            type.TenantId = tenantId;
            _db.Add(type);
        }

        foreach (var type in ContractTypeSeed.Build())
        {
            type.TenantId = tenantId;
            _db.Add(type);
        }

        await _db.SaveChangesAsync(ct);
    }
}
