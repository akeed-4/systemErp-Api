namespace ERP.Service.Services.Shared;

/// <summary>
/// إعدادات المنصة. البريد هنا يحدد المدراء الجذريين (صلاحية كاملة دائماً، ومنهم يبدأ المنح)؛ وبعدها تُمنح صلاحية
/// إدارة المنصة لمستخدمين محددين من قائمة الصلاحيات (انظر <see cref="PlatformAccessService"/>).
/// </summary>
public class PlatformOptions
{
    public const string Section = "Platform";
    /// <summary>بريد مدراء المنصة. فارغ = لا أحد (الافتراضي الآمن). يُضبط عبر user-secrets أو Platform__AdminEmails__0.</summary>
    public string[] AdminEmails { get; set; } = Array.Empty<string>();

    public bool IsAdmin(string? email)
        => !string.IsNullOrWhiteSpace(email)
           && AdminEmails.Any(e => string.Equals(e?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
