using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>حجز مخزون المستودع لمشاريع المقاولات، وتحويل التخصيص بين المشاريع باعتماد، وأرقام المتاح والمحجوز وقيد التوريد.</summary>
public interface IStockReservationService
{
    Task<List<StockReservationDto>> ListAsync(Guid contractId, CancellationToken ct = default);
    Task<StockReservationDto> ReserveAsync(Guid contractId, SaveStockReservationDto request, CancellationToken ct = default);
    Task<StockReservationDto> ReleaseAsync(Guid contractId, Guid reservationId, ReleaseStockReservationDto request, CancellationToken ct = default);
    Task<StockReservationTransferDto> RequestTransferAsync(Guid contractId, Guid reservationId, RequestReservationTransferDto request, CancellationToken ct = default);
    /// <summary>طلبات التحويل بحالة (أو كلها)، ولمشروع مصدراً أو مستلماً إن حُدد.</summary>
    Task<List<StockReservationTransferDto>> TransfersAsync(string? status, Guid? contractId, CancellationToken ct = default);
    Task<StockReservationTransferDto> ApproveTransferAsync(Guid transferId, ReservationTransferDecisionDto request, CancellationToken ct = default);
    Task<StockReservationTransferDto> RejectTransferAsync(Guid transferId, ReservationTransferDecisionDto request, CancellationToken ct = default);
    Task<List<StockAvailabilityDto>> AvailabilityAsync(IReadOnlyCollection<Guid> productIds, Guid? warehouseId, Guid? contractId, CancellationToken ct = default);
}
