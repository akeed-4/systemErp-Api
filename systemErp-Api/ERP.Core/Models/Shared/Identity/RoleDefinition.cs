namespace ERP.Core.Models.Shared;

/// <summary>
/// تعريف دور في المنشأة. الأدوار الأساسية (owner/admin/general_manager/chief_accountant/sales_rep) افتراضية بلا سجل،
/// ويُنشأ لها سجل فقط عند تعديل وصفها أو تعطيلها. الأدوار المخصّصة سجلات كاملة، وصلاحياتها في
/// <see cref="UserRolePermission"/> بـ RoleId = <see cref="Code"/>.
/// </summary>
public class RoleDefinition : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>الدور الأساسي لمستخدمي هذا الدور (UserRole في الجلسة)، ولا يكون مالكاً أو مديراً لدور مخصّص.</summary>
    public UserRole BaseRole { get; set; } = UserRole.SalesRep;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public string? UpdatedBy { get; set; }
}
