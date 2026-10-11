namespace ERP.Core.DTOs.Shared;

/// <summary>إنشاء مستخدم داخل المنشأة (يُضيفه المدير من شاشة الصلاحيات).</summary>
public class CreateUserRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.SalesRep;
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
}
