using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarShowroomSettingsService
{
    Task<CarShowroomSettingsDto> GetAsync(CancellationToken ct = default);
    Task<CarShowroomSettingsDto> UpdateAsync(CarShowroomSettingsDto dto, CancellationToken ct = default);
}

public interface IVehicleReservationService
{
    /// <summary>status اختياري: active | released | expired | converted. الحجوزات المنتهية بلا عربون أو عقد تُفك أولاً.</summary>
    Task<PagedResult<VehicleReservationDto>> ListAsync(PaginationParams query, Guid? vehicleId = null, CancellationToken ct = default);
    Task<VehicleReservationDto> CreateAsync(CreateVehicleReservationDto dto, CancellationToken ct = default);
    Task<VehicleReservationDto> ExtendAsync(Guid id, ExtendVehicleReservationDto dto, CancellationToken ct = default);
    /// <summary>يلغي الحجز. إن كان عليه عربون مفتوح يُرد أو يُصادَر حسب الطلب وإلا سياسة الإعدادات.</summary>
    Task<VehicleReservationDto> CancelAsync(Guid id, CancelVehicleReservationDto? request, CancellationToken ct = default);
}

public interface ITammOperationService
{
    Task<PagedResult<TammOperationDto>> ListAsync(PaginationParams query, Guid? vehicleId = null, CancellationToken ct = default);
    Task<TammOperationDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<TammOperationDto> CreateAsync(CreateTammOperationDto dto, CancellationToken ct = default);
    /// <summary>
    /// يحدّث حالة العملية. النجاح ينشئ مصروفها الفعلي مرة واحدة (تكرار الطلب لا يكرره)؛ الفشل والمجهول لا ينشئان شيئاً.
    /// العملية المنتهية (ناجحة/فاشلة) لا تتغيّر حالتها.
    /// </summary>
    Task<TammOperationDto> UpdateStatusAsync(Guid id, UpdateTammOperationStatusDto dto, CancellationToken ct = default);
}

public interface ITammWalletService
{
    Task<TammWalletDto> GetAsync(PaginationParams query, CancellationToken ct = default);
    /// <summary>شحن المحفظة: من حساب البنك/الصندوق إلى حساب المحفظة.</summary>
    Task<TammWalletTransactionDto> TopUpAsync(TammWalletTopUpDto dto, CancellationToken ct = default);
    /// <summary>مطابقة الرصيد الدفتري مع كشف تم: يُسجَّل الفرق، ويُسوّى بقيد إن طُلب.</summary>
    Task<TammWalletTransactionDto> ReconcileAsync(TammWalletReconcileDto dto, CancellationToken ct = default);
}

public interface ISaleProfitService
{
    Task<SaleProfitPanelDto> GetPanelAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>الأسطر الظاهرة للعميل في عرض السعر والفاتورة (رسوم عليه وإضافات).</summary>
    Task<List<SaleCustomerChargeDto>> GetCustomerChargesAsync(Guid contractId, CancellationToken ct = default);
}
