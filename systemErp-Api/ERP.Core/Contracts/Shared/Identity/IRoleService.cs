using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>أدوار المنشأة: الأساسية (افتراضية) والمخصّصة، والنسخ والتعطيل.</summary>
public interface IRoleService
{
    Task<List<RoleDto>> ListAsync(CancellationToken ct = default);
    Task<RoleDto> GetAsync(string code, CancellationToken ct = default);
    /// <summary>دور مخصّص جديد بلا صلاحيات.</summary>
    Task<RoleDto> CreateAsync(SaveRoleDto dto, CancellationToken ct = default);
    /// <summary>دور مخصّص جديد بنسخة من صلاحيات دور قائم ونطاقه وحده.</summary>
    Task<RoleDto> CopyAsync(string sourceCode, SaveRoleDto dto, CancellationToken ct = default);
    Task<RoleDto> UpdateAsync(string code, SaveRoleDto dto, CancellationToken ct = default);
    Task<RoleDto> SetActiveAsync(string code, bool active, CancellationToken ct = default);
}
