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

        // الموقع الرئيسي بحساب مخزونه الخاص تحت 114 (كل موقع يُضاف لاحقاً يُنشأ له حسابه بالترقيم نفسه)
        var warehouse = new Warehouse
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            Code = "WH-001", NameAr = "المستودع الرئيسي", NameEn = "Main Warehouse", IsDefault = true,
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
