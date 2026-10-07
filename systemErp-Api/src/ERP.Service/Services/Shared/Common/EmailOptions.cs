namespace ERP.Service.Services.Shared;

/// <summary>
/// خادم البريد الصادر (SMTP). كلمة المرور سرّ: تُضبط عبر user-secrets أو متغيّر البيئة Email__Password ولا تُكتب في الملفات.
/// مع Gmail تُستعمل «كلمة مرور التطبيق» (App Password) لا كلمة مرور الحساب.
/// </summary>
public class EmailOptions
{
    public const string Section = "Email";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 20;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(UserName)
                                && !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(FromAddress);
}
