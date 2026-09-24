using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Accounting.Domain;
using Erp.Modules.Accounting.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Application;

/// <summary>
/// The chart of accounts every tenant starts with. It keeps the frontend's own codes (1/11/111/1111/112/113/2/21/211/212/3/4/41/42/5)
/// and adds the leaves the posting rules need (input VAT, COGS, clearing accounts, …). Output VAT is 212, the code in the frontend
/// chart; the conflicting 213/2104 codes used in some screens are not reproduced (§14 D13).
/// </summary>
internal static class DefaultChartOfAccounts
{
    internal sealed record Definition(string Code, string? Parent, string NameAr, string NameEn, AccountType Type, bool DebitNature, PostingPurpose? Purpose = null);

    public static readonly Definition[] Accounts =
    [
        new("1", null, "الأصول", "Assets", AccountType.Asset, true),
        new("11", "1", "الأصول المتداولة", "Current Assets", AccountType.Asset, true),
        new("111", "11", "النقدية وما في حكمها", "Cash & Cash Equivalents", AccountType.Asset, true, PostingPurpose.BankControl),
        new("1111", "111", "الصندوق الرئيسي", "Main Cash Vault", AccountType.Asset, true, PostingPurpose.CashOnHand),
        new("1112", "111", "حساب تسوية الشبكة والبطاقات", "Card & Mada Clearing", AccountType.Asset, true, PostingPurpose.CardClearing),
        new("112", "11", "العملاء والمدينون التجاريون", "Accounts Receivable", AccountType.Asset, true, PostingPurpose.CustomerControl),
        new("113", "11", "مخزون السيارات والمركبات", "Vehicle Inventory", AccountType.Asset, true, PostingPurpose.VehicleInventory),
        new("114", "11", "مخزون البضائع", "Merchandise Inventory", AccountType.Asset, true, PostingPurpose.Inventory),
        new("115", "11", "ضريبة القيمة المضافة المدخلات", "Input VAT", AccountType.Asset, true, PostingPurpose.InputVat),
        new("116", "11", "محتجزات ضمان حسن التنفيذ", "Retention Receivable", AccountType.Asset, true, PostingPurpose.RetentionReceivable),
        new("117", "11", "مستحقات جهات التمويل", "Financing Bank Receivable", AccountType.Asset, true, PostingPurpose.FinancingBankReceivable),
        new("12", "1", "الأصول غير المتداولة", "Non-current Assets", AccountType.Asset, true),
        new("121", "12", "الأصول الثابتة", "Fixed Assets", AccountType.Asset, true, PostingPurpose.FixedAssets),
        new("122", "12", "مجمع الإهلاك", "Accumulated Depreciation", AccountType.Asset, false, PostingPurpose.AccumulatedDepreciation),

        new("2", null, "الالتزامات (الخصوم)", "Liabilities", AccountType.Liability, false),
        new("21", "2", "الالتزامات المتداولة", "Current Liabilities", AccountType.Liability, false),
        new("211", "21", "الموردون والدائنون التجاريون", "Accounts Payable", AccountType.Liability, false, PostingPurpose.SupplierControl),
        new("212", "21", "ضريبة القيمة المضافة المستحقة (ZATCA)", "VAT Payable (ZATCA)", AccountType.Liability, false, PostingPurpose.OutputVat),
        new("213", "21", "دفعات مقدمة من العملاء", "Customer Advances", AccountType.Liability, false, PostingPurpose.CustomerAdvances),
        new("214", "21", "بضاعة مستلمة لم تفوتر", "Goods Received Not Invoiced", AccountType.Liability, false, PostingPurpose.GoodsReceivedNotInvoiced),
        new("215", "21", "مستحقات جهات التمويل", "Financing Bank Payable", AccountType.Liability, false, PostingPurpose.FinancingBankPayable),
        new("216", "21", "التزامات نقاط الولاء", "Loyalty Liability", AccountType.Liability, false, PostingPurpose.LoyaltyLiability),
        new("217", "21", "أرصدة دائنة للعملاء", "Customer Credit", AccountType.Liability, false, PostingPurpose.CustomerCredit),

        new("3", null, "حقوق الملكية", "Equity", AccountType.Equity, false),
        new("31", "3", "رأس المال", "Capital", AccountType.Equity, false),
        new("32", "3", "الأرباح المبقاة", "Retained Earnings", AccountType.Equity, false),
        new("33", "3", "أرصدة افتتاحية", "Opening Balances", AccountType.Equity, false, PostingPurpose.OpeningBalances),

        new("4", null, "الإيرادات والمبيعات", "Revenue", AccountType.Revenue, false),
        new("41", "4", "إيرادات بيع سيارات جديدة", "New Car Sales Revenue", AccountType.Revenue, false, PostingPurpose.CarSalesRevenueNew),
        new("42", "4", "إيرادات هامش ربح السيارات المستعملة", "Used Car Margin Revenue", AccountType.Revenue, false, PostingPurpose.UsedCarMarginRevenue),
        new("43", "4", "إيرادات المبيعات", "Sales Revenue", AccountType.Revenue, false, PostingPurpose.SalesRevenue),
        new("44", "4", "إيرادات نقاط البيع", "POS Revenue", AccountType.Revenue, false, PostingPurpose.PosRevenue),
        new("45", "4", "إيرادات العقود والخدمات", "Contract Revenue", AccountType.Revenue, false, PostingPurpose.ContractRevenue),
        new("46", "4", "مردودات المبيعات", "Sales Returns", AccountType.Revenue, true, PostingPurpose.SalesReturns),
        new("47", "4", "خصم مسموح به", "Sales Discounts", AccountType.Revenue, true, PostingPurpose.SalesDiscount),

        new("5", null, "المصروفات وتكلفة المبيعات", "Expenses & COGS", AccountType.Expense, true),
        new("51", "5", "تكلفة البضاعة المباعة", "Cost of Goods Sold", AccountType.Expense, true, PostingPurpose.CostOfGoodsSold),
        new("52", "5", "تكلفة السيارات المباعة", "Cost of Vehicles Sold", AccountType.Expense, true, PostingPurpose.CostOfVehiclesSold),
        new("53", "5", "تسويات المخزون", "Inventory Adjustments", AccountType.Expense, true, PostingPurpose.InventoryAdjustment),
        new("54", "5", "فروقات أسعار الشراء", "Purchase Price Variance", AccountType.Expense, true, PostingPurpose.PurchasePriceVariance),
        new("55", "5", "عجز وزيادة الصندوق", "Cash Over / Short", AccountType.Expense, true, PostingPurpose.CashOverShort),
        new("56", "5", "مصروف الإهلاك", "Depreciation Expense", AccountType.Expense, true, PostingPurpose.DepreciationExpense),
        new("57", "5", "المصروفات العمومية والإدارية", "General & Administrative Expenses", AccountType.Expense, true, PostingPurpose.GeneralExpenses),
    ];
}

internal sealed class AccountingSeeder(AccountingDbContext db) : IModuleSeeder
{
    public int Order => 50;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.Accounts.AnyAsync(cancellationToken))
        {
            return;
        }

        var byCode = new Dictionary<string, Account>(StringComparer.Ordinal);
        foreach (var d in DefaultChartOfAccounts.Accounts)
        {
            var parent = d.Parent is null ? null : byCode[d.Parent];
            var account = new Account(d.Code, d.NameAr, d.NameEn, d.Type, parent, d.DebitNature, isSystem: true);
            byCode[d.Code] = account;
            db.Accounts.Add(account);
            if (d.Purpose is { } purpose)
            {
                db.PostingMappings.Add(new PostingAccountMapping(purpose, account.Id));
            }
        }

        var year = DateTime.UtcNow.Year;
        for (var month = 1; month <= 12; month++)
        {
            db.FiscalPeriods.Add(new FiscalPeriod(year, month));
        }

        db.CostCenters.Add(new CostCenter("CC-HO", "المركز الرئيسي", "Head Office", "مركز التكلفة الافتراضي"));
    }
}
