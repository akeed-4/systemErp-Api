using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>إعلانات المنصة للعملاء: إدارة مدير المنصة، وما يظهر الآن للمنشأة الحالية.</summary>
public interface IPlatformAnnouncementService
{
    Task<List<PlatformAnnouncementDto>> ListAsync(CancellationToken ct = default);
    Task<PlatformAnnouncementDto> CreateAsync(SavePlatformAnnouncementRequestDto request, CancellationToken ct = default);
    Task<PlatformAnnouncementDto> UpdateAsync(Guid id, SavePlatformAnnouncementRequestDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>الإعلانات الفعّالة الآن والمستهدِفة للمنشأة الحالية (بحالة اشتراكها وباقتها)، الأعلى أولوية أولاً.</summary>
    Task<List<PlatformAnnouncementDto>> ActiveForCurrentTenantAsync(CancellationToken ct = default);
}
