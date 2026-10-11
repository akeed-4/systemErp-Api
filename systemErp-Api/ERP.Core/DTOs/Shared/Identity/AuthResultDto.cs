namespace ERP.Core.DTOs.Shared;

/// <summary>استجابة الدخول/التسجيل كما تتوقعها الواجهة: { success, message, token, user, tenant }.</summary>
public class AuthResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
    public int ExpiresIn { get; set; }
    public Guid? TenantId { get; set; }
    public UserDto? User { get; set; }
    public TenantDto? Tenant { get; set; }
}
