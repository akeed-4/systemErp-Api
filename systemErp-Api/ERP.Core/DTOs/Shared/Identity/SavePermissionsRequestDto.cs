namespace ERP.Core.DTOs.Shared;

public class SavePermissionsRequestDto
{
    public Guid? UserId { get; set; }
    public string? RoleId { get; set; }
    public List<ScreenPermissionDto> Permissions { get; set; } = new();
}
