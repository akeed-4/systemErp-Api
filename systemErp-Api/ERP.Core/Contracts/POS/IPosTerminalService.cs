using ERP.Core.DTOs.POS;

namespace ERP.Core.Contracts.POS;

public interface IPosTerminalService
{
    Task<List<PosTerminalDto>> ListAsync(bool availableToCurrentUser, CancellationToken ct = default);
    Task<List<PosTerminalUserDto>> UsersAsync(CancellationToken ct = default);
    Task<PosTerminalDto> SaveAsync(Guid? id, SavePosTerminalDto request, CancellationToken ct = default);
}
