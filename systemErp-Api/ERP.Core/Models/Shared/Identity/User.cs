
namespace ERP.Core.Models.Shared;

public class User : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Owner;
    /// <summary>دور مخصّص (<see cref="RoleDefinition"/>) تُقرأ منه الصلاحيات بدل الدور الأساسي.</summary>
    public Guid? CustomRoleId { get; set; }
    public string? AvatarInitials { get; set; }
    public string? AvatarUrl { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
}
