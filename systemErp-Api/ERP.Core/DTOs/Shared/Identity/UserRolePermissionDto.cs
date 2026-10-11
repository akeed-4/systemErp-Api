namespace ERP.Core.DTOs.Shared;

public partial class UserRolePermissionDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UserId { get; set; }
    public string? RoleId { get; set; }
    public List<ScreenPermissionItemDto> Permissions { get; set; } = new();
}
