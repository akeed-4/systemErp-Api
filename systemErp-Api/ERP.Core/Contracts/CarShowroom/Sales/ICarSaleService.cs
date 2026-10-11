using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarSaleService
{
    /// <summary>cycleType اختياري لتصفية دورة البيع (أفراد/شركات/بنوك/تقسيط).</summary>
    Task<PagedResult<CarSalesContractDto>> ListAsync(PaginationParams query, CarSalesCycleType? cycleType = null, CancellationToken ct = default);
    /// <summary>نفس القائمة بخيارات DevExtreme (filter/sort/skip/take/totalSummary) منفَّذة في قاعدة البيانات.</summary>
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CarSalesCycleType? cycleType = null, CancellationToken ct = default);
    Task<CarSalesContractDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CarSalesContractDto> CreateAsync(CreateCarSalesContractDto request, CancellationToken ct = default);
    /// <summary>تعديل العقد قبل الفوترة؛ السعر والضريبة يُحسبان في السرفر.</summary>
    Task<CarSalesContractDto> UpdateAsync(Guid id, UpdateCarSalesContractDto request, CancellationToken ct = default);
    /// <summary>ينقل العقد للمرحلة التالية (5 مراحل) مع آثارها: حجز المركبة، التسليم، الفاتورة والقيد.</summary>
    Task<CarSalesContractDto> AdvanceStatusAsync(Guid id, AdvanceSalesContractRequestDto request, CancellationToken ct = default);
    Task<CarSalesContractDto> CancelAsync(Guid id, string? reason, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>يُكمل عقدًا قائمًا من مرحلته الحالية حتى الفوترة في معاملة واحدة (بيانات التسليم الفارغة تُملأ من المشتري).</summary>
    Task<CarSalesContractDto> CompleteAsync(Guid id, CompleteSalesContractRequestDto? request, CancellationToken ct = default);
    /// <summary>بيع سريع: إنشاء العقد ثم إكماله حتى الفاتورة في معاملة واحدة.</summary>
    Task<CarSalesContractDto> QuickSaleAsync(QuickSaleRequestDto request, CancellationToken ct = default);
}
