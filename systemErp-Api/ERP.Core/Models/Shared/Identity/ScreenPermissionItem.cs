
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
    /// <summary>
    /// كل الإجراءات الممنوحة برموزها (view,create,...,post,print) مفصولة بفواصل. null = سجل قديم قبل الإجراءات المستقلة:
    /// تُشتق إجراءاته من الأعمدة الخمسة بحسب كتالوج الصلاحيات.
    /// </summary>
    public string? Actions { get; set; }

    public virtual UserRolePermission UserRolePermission { get; set; } = null!;
}
