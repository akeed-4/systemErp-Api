namespace ERP.Core.DTOs.Shared;

public class LoginRequestDto
{
    /// <summary>البريد الإلكتروني (أو اسم المستخدم كما تُرسله الواجهة في حقل email).</summary>
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
