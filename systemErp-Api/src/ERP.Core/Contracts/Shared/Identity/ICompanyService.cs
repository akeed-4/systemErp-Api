using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>المنشأة (Company) الحالية وإعدادات الربط الإلكتروني.</summary>
public interface ICompanyService
{
    Task<TenantDto> GetCurrentAsync(CancellationToken ct = default);
    Task<TenantDto> UpdateAsync(UpdateTenantDto request, CancellationToken ct = default);
    Task<TenantDto> UpdateZatcaConfigAsync(UpdateZatcaConfigDto request, CancellationToken ct = default);
    Task<ZatcaConnectionTestResultDto> TestZatcaConnectionAsync(CancellationToken ct = default);
}
