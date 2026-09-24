using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IUserService
{
    Task<List<UserDto>> ListAsync(CancellationToken ct = default);
    Task<UserDto> GetCurrentAsync(CancellationToken ct = default);
    Task<UserDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequestDto request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequestDto request, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(UpdateProfileDto request, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, CancellationToken ct = default);
}
