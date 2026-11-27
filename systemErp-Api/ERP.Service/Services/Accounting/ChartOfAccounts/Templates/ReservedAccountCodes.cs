namespace ERP.Service.Services.Accounting;

/// <summary>
/// الأرقام المحجوزة: كل حساب في الشجرة الافتراضية القديمة (<see cref="DefaultAccounts.Chart"/>) وافتراضيات إعدادات المعرض.
/// يقرؤها الكود برقمها (ثوابت DefaultAccounts، و<c>DefaultAccounts.EnsureAsync</c> الذي ينشئ الناقص منها وقت التشغيل)، فلا تأخذ في
/// أي قالب معنى آخر: الرقم المحجوز في القالب بنوعه وأبيه في الشجرة القديمة (الاسم يُعدَّل)، ولا يولّد المحرك رقماً محجوزاً لحساب آخر.
/// </summary>
internal static class ReservedAccountCodes
{
    public sealed record Reserved(string Code, AccountCategory Type, string? ParentCode, string NameAr, string NameEn);

    private static readonly Dictionary<string, Reserved> ByCode = DefaultAccounts.Chart
        .ToDictionary(a => a.Code, a => new Reserved(a.Code, a.Type, a.Parent, a.Ar, a.En));

    /// <summary>افتراضيات حسابات المعرض (CarShowroomSettings) خارج الشجرة لو وُجدت: محجوزة كذلك.</summary>
    private static readonly HashSet<string> ShowroomDefaults = typeof(CarShowroomSettings).GetProperties()
        .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Account", StringComparison.Ordinal))
        .Select(p => p.GetValue(new CarShowroomSettings()) as string)
        .OfType<string>().Where(c => c.Length > 0).ToHashSet();

    public static bool IsReserved(string? code) => code != null && (ByCode.ContainsKey(code) || ShowroomDefaults.Contains(code));

    public static Reserved? Find(string? code) => code != null && ByCode.TryGetValue(code, out var r) ? r : null;
}
