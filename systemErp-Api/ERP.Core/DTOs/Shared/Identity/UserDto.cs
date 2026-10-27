namespace ERP.Core.DTOs.Shared;

public partial class UserDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Owner;
    public Guid? CustomRoleId { get; set; }
    public string? AvatarInitials { get; set; }
    public string? AvatarUrl { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    /// <summary>المواقع المسموحة؛ فارغة = كل المواقع.</summary>
    public List<Guid> AllowedWarehouseIds { get; set; } = new();
}
