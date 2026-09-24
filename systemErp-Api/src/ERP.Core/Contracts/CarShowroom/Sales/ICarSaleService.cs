using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarSaleService
{
    Task<PagedResult<CarSalesContractDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<CarSalesContractDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CarSalesContractDto> CreateAsync(CreateCarSalesContractDto request, CancellationToken ct = default);
    /// <summary>تعديل العقد قبل الفوترة؛ السعر والضريبة يُحسبان في الخادم.</summary>
    Task<CarSalesContractDto> UpdateAsync(Guid id, UpdateCarSalesContractDto request, CancellationToken ct = default);
    /// <summary>ينقل العقد للمرحلة التالية (5 مراحل) مع آثارها: حجز المركبة، التسليم، الفاتورة والقيد.</summary>
    Task<CarSalesContractDto> AdvanceStatusAsync(Guid id, AdvanceSalesContractRequestDto request, CancellationToken ct = default);
    Task<CarSalesContractDto> CancelAsync(Guid id, string? reason, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
