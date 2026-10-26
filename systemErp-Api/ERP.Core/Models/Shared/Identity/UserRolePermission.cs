
namespace ERP.Core.Models.Shared;

/// <summary>
/// صلاحيات الشاشات (RBAC) لمستخدم محدد أو لدور كامل. عند وجود <see cref="UserId"/> فهي
/// تخصيص خاص بمستخدم واحد يتفوق على صلاحيات دوره الافتراضية؛ وإلا فهي الصلاحيات الافتراضية للدور.
/// </summary>
public class UserRolePermission : BaseEntity
{
    public Guid? UserId { get; set; }
    public string? RoleId { get; set; }
    /// <summary>نطاق الوصول للمستندات ذات النطاق (<see cref="IScopedRecord"/>).</summary>
    public DataScope Scope { get; set; } = DataScope.All;
    /// <summary>معرّفات المستودعات (الفروع) مفصولة بفواصل عند <see cref="DataScope.Branches"/>.</summary>
    public string? ScopeWarehouseIds { get; set; }
    /// <summary>أقصى مبلغ يعتمده الدور/المستخدم؛ null = بلا حد.</summary>
    public decimal? ApprovalLimit { get; set; }

    public virtual ICollection<ScreenPermissionItem> Permissions { get; set; } = new List<ScreenPermissionItem>();
}
