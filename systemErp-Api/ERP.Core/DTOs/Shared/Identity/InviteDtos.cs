namespace ERP.Core.DTOs.Shared;

/// <summary>ما يُعرض لصاحب رابط الدعوة قبل تعيين كلمة المرور.</summary>
public class InviteInfoDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
}

public class AcceptInviteRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
