using System.Reflection;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تغيير رقم حساب لم يُرحَّل عليه (القيود تمنع تغيير رقم المستخدم): الإعدادات التي تحمل الرقم القديم تتبعه في المعاملة نفسها، فلا ينقطع
/// ربط وظيفي بصمت — أدوار إعدادات الحسابات، وحسابات المواقع، وإعدادات المعرض. الربط العام (AccountBinding) بمعرّف الحساب فلا يتأثر.
/// </summary>
internal static class AccountCodeReferences
{
    private static readonly PropertyInfo[] WarehouseCodes = typeof(Warehouse).GetProperties()
        .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("AccountCode", StringComparison.Ordinal) && p.CanWrite).ToArray();
    private static readonly PropertyInfo[] ShowroomCodes = typeof(CarShowroomSettings).GetProperties()
        .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Account", StringComparison.Ordinal) && p.CanWrite).ToArray();

    public static async Task RenameAsync(ErpDbContext db, string oldCode, string newCode, CancellationToken ct)
    {
        if (oldCode == newCode) return;

        foreach (var settings in await db.Set<AccountingSettings>().ToListAsync(ct))
            foreach (var role in PostingAccounts.Roles)
                if (role.Get(settings) == oldCode) role.Set(settings, newCode);

        foreach (var warehouse in await db.Set<Warehouse>().ToListAsync(ct))
            foreach (var p in WarehouseCodes)
                if ((string?)p.GetValue(warehouse) == oldCode) p.SetValue(warehouse, newCode);

        foreach (var showroom in await db.Set<CarShowroomSettings>().ToListAsync(ct))
            foreach (var p in ShowroomCodes)
                if ((string?)p.GetValue(showroom) == oldCode) p.SetValue(showroom, newCode);

        AccountKeyResolver.Invalidate(db.CurrentTenantId);
    }
}
