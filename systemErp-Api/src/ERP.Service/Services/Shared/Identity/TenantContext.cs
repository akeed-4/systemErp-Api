using System.Security.Claims;
using ERP.Core.Contracts.Shared;
using Microsoft.AspNetCore.Http;

namespace ERP.Service.Services.Shared;

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}
