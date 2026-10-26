namespace ERP.Core.DTOs.Shared;

public class SavePermissionsRequestDto
{
    public Guid? UserId { get; set; }
    public string? RoleId { get; set; }
    public List<ScreenPermissionDto> Permissions { get; set; } = new();
    /// <summary>نطاق الوصول وحد الاعتماد؛ null = يبقى المحفوظ كما هو.</summary>
    public PermissionSettingsDto? Settings { get; set; }
}
