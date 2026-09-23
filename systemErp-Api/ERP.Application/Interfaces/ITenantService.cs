namespace ERP.Application.Interfaces;

public interface ITenantService
{
    Guid? CurrentTenantId { get; }
    void SetCurrentTenant(Guid tenantId);
}
