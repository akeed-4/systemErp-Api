using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// طريقة دفع نقدية لكل صندوق يُعرَّف في شجرة الحسابات: حساب فرعي جديد تحت الصندوق/النقدية (111) غير مرتبط بكيان تُنشأ له
/// في المعاملة نفسها طريقة دفع من نوع «نقدي» باسمه ومرتبطة بحسابه، فيظهر مباشرة في قوائم الدفع بالمستندات.
/// <list type="bullet">
/// <item>رمز الطريقة <c>BOX-</c> + رمز الحساب (BOX-1112)، ويتخطى رمزاً مستخدماً.</item>
/// <item>إن لم توجد أي طريقة نقدية بعد تُنشأ معها طريقة الصندوق الرئيسي (1111) أولاً، فيبقى النقد بلا طريقة مختارة على الصندوق
/// الرئيسي كما كان (الافتراضي = أقدم طريقة نقدية، <see cref="TreasuryResolver"/>).</item>
/// <item>صناديق المواقع (<see cref="SiteAccounts"/>) لا تُنشأ لها طريقة: النقد في الموقع يُقيَّد على صندوقه تلقائياً.</item>
/// </list>
/// اسم الطريقة وحالتها ورمز حسابها تتبع الحساب عند تعديله، وتُحذف مع حذفه. الحفظ على المستدعي.
/// </summary>
public static class CashBoxPaymentMethods
{
    public const string CodePrefix = "BOX-";

    public static string CodeOf(string accountCode) => CodePrefix + accountCode;

    /// <summary>هل الحساب الجديد صندوق: فرعي (لا رئيسي)، غير مرتبط بكيان، وأبوه الصندوق/النقدية (111) أو أحد فروعه.</summary>
    public static async Task<bool> IsCashBoxAsync(ErpDbContext db, Account account, CancellationToken ct)
    {
        if (account.IsGroup || account.LinkedEntityType is not (null or LinkedEntityType.General) || string.IsNullOrEmpty(account.ParentCode)) return false;
        // مجموعة البنوك 111 (110 حساب للمستخدم): فروعها بنوك لا صناديق
        if (await TreasuryAccounts.BanksGroupAsync(db, ct) == DefaultAccounts.CashBoxes) return false;
        var parents = await db.Set<Account>().AsNoTracking().Select(a => new { a.Code, a.ParentCode }).ToDictionaryAsync(a => a.Code, a => a.ParentCode, ct);
        var seen = new HashSet<string>();
        for (string? c = account.ParentCode; c != null && seen.Add(c); c = parents.GetValueOrDefault(c))
            if (c == DefaultAccounts.CashBoxes) return true;
        return false;
    }

    /// <summary>ينشئ طريقة الدفع للصندوق الجديد (وطريقة الصندوق الرئيسي إن لم توجد طريقة نقدية بعد).</summary>
    public static async Task EnsureForNewBoxAsync(ErpDbContext db, Account box, CancellationToken ct)
    {
        if (await db.Set<PaymentMethodItem>().AnyAsync(m => m.LinkedAccountCode == box.Code, ct)) return;
        if (box.Code != DefaultAccounts.Cash && !await db.Set<PaymentMethodItem>().AnyAsync(m => m.Type == "cash", ct)
            && await db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.Code == DefaultAccounts.Cash, ct) is { } main
            && !await db.Set<Account>().AnyAsync(a => a.ParentCode == main.Code, ct)
            && !await db.Set<PaymentMethodItem>().AnyAsync(m => m.LinkedAccountCode == main.Code, ct))
            await AddAsync(db, main, ct);
        await AddAsync(db, box, ct);
    }

    private static async Task AddAsync(ErpDbContext db, Account account, CancellationToken ct)
    {
        var code = CodeOf(account.Code);
        for (var i = 2; db.Set<PaymentMethodItem>().Local.Any(m => m.Code == code) || await db.Set<PaymentMethodItem>().AnyAsync(m => m.Code == code, ct); i++)
            code = $"{CodeOf(account.Code)}-{i}";
        var nameAr = account.NameAr.Trim();
        db.Add(new PaymentMethodItem
        {
            Code = code,
            NameAr = nameAr,
            NameEn = string.IsNullOrWhiteSpace(account.NameEn) ? nameAr : account.NameEn.Trim(),
            Type = "cash",
            LinkedAccountCode = account.Code,
            LinkedAccountName = nameAr,
            Icon = "payments",
            Status = account.IsActive ? "active" : "inactive",
        });
    }

    /// <summary>
    /// بعد تعديل الحساب: رمزه واسمه على كل طريقة مرتبطة به، واسم طريقة الصندوق التلقائية (BOX-) وحالتها يتبعان الحساب.
    /// </summary>
    public static async Task SyncAsync(ErpDbContext db, string oldCode, string oldNameAr, Account account, CancellationToken ct)
    {
        var methods = await db.Set<PaymentMethodItem>().Where(m => m.LinkedAccountCode == oldCode).ToListAsync(ct);
        foreach (var m in methods)
        {
            m.LinkedAccountCode = account.Code;
            m.LinkedAccountName = account.NameAr;
            if (!m.Code.StartsWith(CodeOf(oldCode), StringComparison.Ordinal)) continue;
            if (m.NameAr == oldNameAr)
            {
                m.NameAr = account.NameAr.Trim();
                m.NameEn = string.IsNullOrWhiteSpace(account.NameEn) ? m.NameAr : account.NameEn.Trim();
            }
            m.Status = account.IsActive ? "active" : "inactive";
        }
    }

    /// <summary>حذف الحساب (بلا قيود): تُحذف طرق الدفع المرتبطة به فلا تبقى طريقة على حساب غير موجود.</summary>
    public static async Task RemoveForAccountAsync(ErpDbContext db, string accountCode, CancellationToken ct)
        => db.RemoveRange(await db.Set<PaymentMethodItem>().Where(m => m.LinkedAccountCode == accountCode).ToListAsync(ct));
}
