namespace RayahAccounting.Application.Interfaces;

public interface ITenantService
{
    string CurrentTenantId { get; }
    void SetCurrentTenant(string tenantId);
}
