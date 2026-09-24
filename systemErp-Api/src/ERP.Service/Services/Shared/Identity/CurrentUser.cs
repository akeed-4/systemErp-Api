using System.Security.Claims;
using ERP.Core.Contracts.Shared;
using Microsoft.AspNetCore.Http;

namespace ERP.Service.Services.Shared;

public class CurrentUser : ICurrentUser
{
    public const string TenantClaim = "tenant_id";
    public const string RoleIdClaim = "role_id";

    private readonly ClaimsPrincipal? _principal;

    public CurrentUser(IHttpContextAccessor accessor) => _principal = accessor.HttpContext?.User;

    public bool IsAuthenticated => _principal?.Identity?.IsAuthenticated == true;
    public Guid? UserId => Guid.TryParse(_principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? Name => _principal?.FindFirstValue(ClaimTypes.Name);
    public string? RoleId => _principal?.FindFirstValue(RoleIdClaim);
}
