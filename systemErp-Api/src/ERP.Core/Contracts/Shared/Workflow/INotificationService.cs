using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface INotificationService
{
    Task<AppNotificationDto> NotifyAsync(NotifyRequestDto request, CancellationToken ct = default);
    Task<List<AppNotificationDto>> ListMineAsync(CancellationToken ct = default);
    Task<AppNotificationDto> GetAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task MarkAsReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllAsReadAsync(CancellationToken ct = default);
    Task ClearAllAsync(CancellationToken ct = default);
}
