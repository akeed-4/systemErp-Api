using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarProcurementService
{
    /// <summary>purchaseCycle اختياري: individual | corporate | bank_lease.</summary>
    Task<PagedResult<CarProcurementOrderDto>> ListAsync(PaginationParams query, string? purchaseCycle = null, CancellationToken ct = default);
    /// <summary>نفس القائمة بخيارات DevExtreme (filter/sort/skip/take/totalSummary) منفَّذة في قاعدة البيانات.</summary>
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, string? purchaseCycle = null, CancellationToken ct = default);
    Task<CarProcurementOrderDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<CarProcurementOrderDto> CreateAsync(CreateCarProcurementOrderDto request, CancellationToken ct = default);
    /// <summary>تعديل بيانات الأمر غير المرحلية (بنود، شحن، جمارك...) قبل الفوترة.</summary>
    Task<CarProcurementOrderDto> UpdateAsync(Guid id, UpdateCarProcurementOrderDto request, CancellationToken ct = default);
    /// <summary>ينقل الأمر للمرحلة التالية فقط (7 مراحل) مع الآثار: استلام VIN وإنشاء المركبات، ثم فاتورة الشراء والقيد.</summary>
    Task<CarProcurementOrderDto> AdvanceStageAsync(Guid id, AdvanceProcurementRequestDto request, CancellationToken ct = default);
    Task<CarProcurementOrderDto> RejectAsync(Guid id, RejectProcurementRequestDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>شراء مركبة واحدة بخطوة واحدة: أمر شراء ببند واحد يمرّ بكل المراحل حتى الفاتورة والقيد، في معاملة واحدة.</summary>
    Task<CarProcurementOrderDto> QuickPurchaseAsync(QuickCarPurchaseRequestDto request, CancellationToken ct = default);
}
