
namespace ERP.Core.Models.Shared;

/// <summary>
/// صلاحيات الشاشات (RBAC) لمستخدم محدد أو لدور كامل. عند وجود <see cref="UserId"/> فهي
/// تخصيص خاص بمستخدم واحد يتفوق على صلاحيات دوره الافتراضية؛ وإلا فهي الصلاحيات الافتراضية للدور.
/// </summary>
public class UserRolePermission : BaseEntity
{
    public Guid? UserId { get; set; }
    public string? RoleId { get; set; }

    public virtual ICollection<ScreenPermissionItem> Permissions { get; set; } = new List<ScreenPermissionItem>();
}
