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
                IsDebitNature = type is AccountCategory.Asset or AccountCategory.Expense,
                IsSystem = true,
                Currency = baseCurrencyCode,
            });
        }

        _db.Add(new Currency
        {
            TenantId = tenantId,
            Code = baseCurrencyCode, NameAr = "ريال سعودي", NameEn = "Saudi Riyal", Symbol = "ر.س",
            IsBaseCurrency = true,
        });

        _db.Add(new Warehouse
        {
            TenantId = tenantId,
            Code = "WH-001", NameAr = "المستودع الرئيسي", NameEn = "Main Warehouse", IsDefault = true,
        });

        _db.Add(new CostingPolicy
        {
            TenantId = tenantId,
            Method = CostingMethod.MovingAverage,
            StandardCostVarianceAccountCode = DefaultAccounts.InventoryAdjustment,
        });

        _db.Add(new PosInvoiceSettings { TenantId = tenantId });

        await _db.SaveChangesAsync(ct);
    }
}
