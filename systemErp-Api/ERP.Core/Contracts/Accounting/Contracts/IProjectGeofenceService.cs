using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>نطاق الحضور الميداني لمشروع المقاولات (على ملف المشروع). نطاق السجل والصلاحية يفحصهما المتحكم.</summary>
public interface IProjectGeofenceService
{
    Task<ProjectGeofenceDto> GetAsync(Guid contractId, CancellationToken ct = default);
    Task<ProjectGeofenceDto> SaveAsync(Guid contractId, ProjectGeofenceDto request, CancellationToken ct = default);
}
