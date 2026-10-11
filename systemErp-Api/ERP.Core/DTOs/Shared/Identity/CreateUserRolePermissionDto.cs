namespace ERP.Core.DTOs.Shared;

public partial class CreateUserRolePermissionDto
{
    public Guid? UserId { get; set; }
    public string? RoleId { get; set; }
    public List<ScreenPermissionItemDto> Permissions { get; set; } = new();
}
