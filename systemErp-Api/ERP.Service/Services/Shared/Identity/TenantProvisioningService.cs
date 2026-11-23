using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly ErpDbContext _db;

    public TenantProvisioningService(ErpDbContext db) => _db = db;

    public async Task SeedAsync(Guid tenantId, string baseCurrencyCode, CancellationToken ct = default)
    {
        var levelByCode = new Dictionary<string, int>();
        foreach (var (code, ar, en, type, parent) in DefaultAccounts.Chart)
        {
            var level = parent == null ? 1 : levelByCode[parent] + 1;
            levelByCode[code] = level;
            _db.Add(new Account
            {
                TenantId = tenantId,
                Code = code, NameAr = ar, NameEn = en, Type = type,
                ParentCode = parent, Level = level,
                IsDebitNature = DefaultAccounts.IsDebitNature(code, type),
                IsSystem = true,
                IsGroup = DefaultAccounts.IsGroup(code),
                Currency = baseCurrencyCode,
            });
        }

        // حسابات العقود تحت مجموعات الشجرة الافتراضية نفسها، مربوطة بإعدادات الحسابات (فلا تنبيه إعداد ناقص عند أول فوترة عقد)
        ContractAccountsSetup.Provision(_db, tenantId, baseCurrencyCode, DefaultAccounts.Chart.Select(a => new ContractAccountsSetup.Node
        {
            Code = a.Code, NameAr = a.Ar, NameEn = a.En, Type = a.Type, ParentCode = a.Parent, Level = levelByCode[a.Code],
        }));

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
        var inventoryGroup = DefaultAccounts.Chart.First(a => a.Code == DefaultAccounts.InventoryGroup);
        var siteAccount = new Account
        {
            TenantId = tenantId,
            Code = DefaultAccounts.InventoryGroup + "001",
            NameAr = WarehouseService.InventoryAccountName(warehouse.NameAr), NameEn = WarehouseService.InventoryAccountName(warehouse.NameEn, english: true),
            Type = inventoryGroup.Type, ParentCode = inventoryGroup.Code, Level = levelByCode[inventoryGroup.Code] + 1,
            IsDebitNature = true, Currency = baseCurrencyCode,
            LinkedEntityType = LinkedEntityType.Warehouse, LinkedEntityId = warehouse.Id,
        };
        _db.Add(siteAccount);
        warehouse.InventoryAccountCode = siteAccount.Code;

        // تكلفة إيراد الموقع الرئيسي وعجز مخزونه تحت مجموعة تكلفة المبيعات (أب 511 و513)، وزيادة مخزونه إيراد تحت الإيرادات الأخرى (42)
        var costGroup = DefaultAccounts.Chart.First(a => a.Code == DefaultAccounts.Cogs).Parent!;
        Account SiteAccount(string code, SiteAccounts.Role role, string parent, AccountCategory type) => new()
        {
            TenantId = tenantId, Code = code,
            NameAr = SiteAccounts.Name(role, warehouse.NameAr), NameEn = SiteAccounts.Name(role, warehouse.NameEn, english: true),
            Type = type, ParentCode = parent, Level = levelByCode[parent] + 1,
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
