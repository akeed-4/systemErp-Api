using ERP.Service.Data;
using ERP.Service.Services.CarShowroom;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// صلاحية حساب لدور ترحيل (من إعدادات الحسابات أو حساب مخزون مستودع): موجود، فرعي، ليس حساب طرف ولا نقدية/بنك ولا حساباً
/// من إعدادات معرض السيارات، ونوعه يناسب الدور.
/// </summary>
internal static class AccountRoleRules
{
    /// <summary>رسائل المخالفات لكل (دور، حساب)؛ فارغة إن صلحت كلها.</summary>
    public static async Task<List<string>> ProblemsAsync(ErpDbContext db, IEnumerable<(PostingAccounts.Role Role, string Code)> assignments, CancellationToken ct)
    {
        var chart = await db.Set<Account>().AsNoTracking()
            .Select(a => new { a.Code, a.ParentCode, a.Type, a.LinkedEntityType }).ToDictionaryAsync(a => a.Code, ct);
        var parents = chart.Values.Select(a => a.ParentCode).OfType<string>().ToHashSet();
        bool IsUnder(string code, string root)
        {
            for (var c = code; c != null; c = chart.TryGetValue(c, out var a) ? a.ParentCode : null)
                if (c == root) return true;
            return false;
        }
        // حسابات المعرض تُقرأ من إعداداته (كل خاصية حساب فيها) فلا تُخلط بها حسابات البضاعة العامة
        var car = await SalesCharges.SettingsAsync(db, ct);
        var showroom = typeof(CarShowroomSettings).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Account"))
            .Select(p => p.GetValue(car) as string).OfType<string>().ToHashSet();

        var errors = new List<string>();
        foreach (var (role, code) in assignments)
        {
            if (!chart.TryGetValue(code, out var account)) errors.Add(string.Format(Messages.AccountSettingNotFound, role.Label, code));
            else if (parents.Contains(code)) errors.Add(string.Format(Messages.AccountSettingIsParent, role.Label, code));
            else if (account.LinkedEntityType != null) errors.Add(string.Format(Messages.AccountSettingIsPartyAccount, role.Label, code));
            else if (IsUnder(code, DefaultAccounts.Banks)) errors.Add(string.Format(Messages.AccountSettingIsCashOrBank, role.Label, code));
            else if (showroom.Contains(code)) errors.Add(string.Format(Messages.AccountSettingIsShowroomAccount, role.Label, code));
            else if (!role.Types.Contains(account.Type)) errors.Add(string.Format(Messages.AccountSettingWrongType, role.Label, code));
        }
        return errors;
    }
}
