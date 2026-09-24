using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface IVehicleService : ICrudService<VehicleDto, CreateVehicleDto, UpdateVehicleDto>
{
    /// <summary>حساب الضريبة وفق نمط الاحتساب (يطابق calculateVat في الواجهة).</summary>
    CarVatResult CalculateVat(CalculateCarVatRequestDto request);
    /// <summary>المركبات المتاحة للبيع (لمنتقي المركبات في عقد البيع).</summary>
    Task<List<VehicleDto>> ListAvailableAsync(string? search, CancellationToken ct = default);
}
