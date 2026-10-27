namespace ERP.Core.DTOs.Shared;

public class UpdateUserRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    /// <summary>دور مخصّص: يتقدّم على <see cref="Role"/> ويُضبط الدور الأساسي منه.</summary>
    public Guid? CustomRoleId { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    /// <summary>المواقع (المستودعات/المعارض) المسموحة؛ فارغة = كل المواقع.</summary>
    public List<Guid> AllowedWarehouseIds { get; set; } = new();
    public bool IsActive { get; set; } = true;
}
