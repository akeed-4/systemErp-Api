using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// متابعة توصيل بيانات التسليم المتتبَّعة (من اتفاقية أو فاتورة بيع): الخروج، وتأكيد الاستلام بالكمية الفعلية، والإلغاء بسبب.
/// لا ينشئ أي إجراء هنا حركة مخزون ولا قيداً ولا فاتورة.
/// </summary>
public interface IDeliveryTrackingService
{
    Task<PagedResult<DeliveryNoteDto>> ListAsync(DeliveryTrackingQueryDto query, CancellationToken ct = default);
    Task<DeliveryNoteDto> ReviewLocationAsync(Guid id, ReviewDeliveryLocationDto request, CancellationToken ct = default);
    Task<DeliveryNoteDto> DispatchAsync(Guid id, DispatchDeliveryNoteDto request, CancellationToken ct = default);
    Task<DeliveryNoteDto> ConfirmAsync(Guid id, ConfirmDeliveryDto request, CancellationToken ct = default);
    Task<DeliveryNoteDto> CancelAsync(Guid id, CancelDeliveryNoteDto request, CancellationToken ct = default);
    Task<List<DeliveryNoteEventDto>> GetEventsAsync(Guid id, CancellationToken ct = default);
    /// <summary>تقرير مختصر: عدد البيانات لكل حالة توصيل ومصدر في الفترة.</summary>
    Task<List<DeliveryStatusSummaryDto>> SummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
}
