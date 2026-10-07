namespace ERP.Service.Services.Shared;

/// <summary>إعدادات المنصة. مديرو المنصة يُحدَّدون بالبريد من الإعدادات فقط (لا حقل في قاعدة البيانات يمكن التلاعب به).</summary>
public class PlatformOptions
{
    public const string Section = "Platform";
    /// <summary>بريد مدراء المنصة. فارغ = لا أحد (الافتراضي الآمن). يُضبط عبر user-secrets أو Platform__AdminEmails__0.</summary>
    public string[] AdminEmails { get; set; } = Array.Empty<string>();

    public bool IsAdmin(string? email)
        => !string.IsNullOrWhiteSpace(email)
           && AdminEmails.Any(e => string.Equals(e?.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}
