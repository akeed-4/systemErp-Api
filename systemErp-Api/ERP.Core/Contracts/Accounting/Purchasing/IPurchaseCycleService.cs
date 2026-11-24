using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Purchasing;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// دورة الشراء: طلب شراء معتمد (بلا أسعار) ← طلب عروض أسعار لعدة موردين ← عروض الموردين ← المقارنة ← الترسية (كاملة أو لكل سطر)
/// ← أوامر شراء ← استلام جزئي (سندات استلام) ← فاتورة مورد لكل سند.
/// </summary>
public interface IPurchaseCycleService
{
    Task<List<PurchaseRfqDto>> ListRfqsAsync(Guid? requisitionId, string? status, CancellationToken ct = default);
    Task<PurchaseRfqDto> GetRfqAsync(Guid id, CancellationToken ct = default);
    Task<PurchaseRfqDto> CreateRfqAsync(CreatePurchaseRfqDto request, CancellationToken ct = default);
    Task<PurchaseRfqDto> AddSuppliersAsync(Guid rfqId, AddRfqSuppliersDto request, CancellationToken ct = default);
    Task<PurchaseRfqDto> CancelRfqAsync(Guid rfqId, CancellationToken ct = default);

    Task<QuotationDto> SubmitQuotationAsync(Guid rfqId, SubmitSupplierQuotationDto request, CancellationToken ct = default);
    Task<RfqComparisonDto> CompareAsync(Guid rfqId, CancellationToken ct = default);
    Task<PurchaseRfqDto> AwardAsync(Guid rfqId, AwardRfqDto request, CancellationToken ct = default);

    /// <summary>أوامر الشراء الناتجة عن الترسية (للقراءة): اختيارياً بحالة أو طلب شراء أو طلب عروض.</summary>
    Task<List<PurchaseOrderListDto>> ListOrdersAsync(string? status, Guid? requisitionId, Guid? rfqId, CancellationToken ct = default);

    Task<PurchaseOrderTrackingDto> OrderTrackingAsync(Guid orderId, CancellationToken ct = default);
    Task<DeliveryNoteDto> ReceiveAsync(Guid orderId, ReceivePurchaseOrderDto request, CancellationToken ct = default);
    Task<InvoiceDto> InvoiceReceiptAsync(Guid orderId, Guid deliveryNoteId, DateTime? date, CancellationToken ct = default);
    /// <summary>فاتورة مورد واحدة لعدة سندات استلام غير مفوترة من الأمر.</summary>
    Task<InvoiceDto> InvoiceReceiptsAsync(Guid orderId, InvoicePurchaseReceiptsDto request, CancellationToken ct = default);
}
