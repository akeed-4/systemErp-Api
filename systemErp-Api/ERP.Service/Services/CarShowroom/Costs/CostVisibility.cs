using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CostVisibility : ICostVisibility
{
    private readonly IPermissionService _permissions;
    private Task<bool>? _canView;

    public CostVisibility(IPermissionService permissions) => _permissions = permissions;

    public Task<bool> CanViewAsync(CancellationToken ct = default)
        => _canView ??= _permissions.HasPermissionAsync(PermissionService.CarCostViewScreenId, ScreenAction.View, ct);
}
