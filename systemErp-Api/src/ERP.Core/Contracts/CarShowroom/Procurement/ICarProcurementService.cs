using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarProcurementService
{
    Task<PagedResult<CarProcurementOrderDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<CarProcurementOrderDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CarProcurementOrderDto> CreateAsync(CreateCarProcurementOrderDto request, CancellationToken ct = default);
    /// <summary>تعديل بيانات الأمر غير المرحلية (بنود، شحن، جمارك...) قبل الفوترة.</summary>
    Task<CarProcurementOrderDto> UpdateAsync(Guid id, UpdateCarProcurementOrderDto request, CancellationToken ct = default);
    /// <summary>ينقل الأمر للمرحلة التالية فقط (7 مراحل) مع الآثار: استلام VIN وإنشاء المركبات، ثم فاتورة الشراء والقيد.</summary>
    Task<CarProcurementOrderDto> AdvanceStageAsync(Guid id, AdvanceProcurementRequestDto request, CancellationToken ct = default);
    Task<CarProcurementOrderDto> RejectAsync(Guid id, RejectProcurementRequestDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
