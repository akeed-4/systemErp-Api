using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IPermissionService
{
    List<ScreenDefinitionDto> GetScreens();
    /// <summary>صلاحيات مستخدم محدد، ثم دوره، ثم الافتراضي (كما في PermissionService بالواجهة).</summary>
    Task<List<ScreenPermissionDto>> GetForUserOrRoleAsync(Guid? userId, string? roleId, CancellationToken ct = default);
    /// <summary>
    /// شبكة تعديل الصلاحيات: الصلاحيات نفسها، ويُضاف لها سطر إدارة المنصة عند تعديل مستخدم محدد من قِبل مدير منصة فقط.
    /// </summary>
    Task<List<ScreenPermissionDto>> GetForEditingAsync(Guid? userId, string? roleId, CancellationToken ct = default);
    Task SaveAsync(SavePermissionsRequestDto request, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(string screenId, ScreenAction action, CancellationToken ct = default);
}
