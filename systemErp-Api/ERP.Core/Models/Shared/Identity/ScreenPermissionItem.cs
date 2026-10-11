
namespace ERP.Core.Models.Shared;

public class ScreenPermissionItem : BaseEntity
{
    public Guid UserRolePermissionId { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanApprove { get; set; }

    public virtual UserRolePermission UserRolePermission { get; set; } = null!;
}
