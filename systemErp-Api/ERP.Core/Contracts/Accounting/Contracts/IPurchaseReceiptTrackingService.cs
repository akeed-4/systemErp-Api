using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// متابعة توريد المشتريات (شاشة <c>purchase-receipt-tracking</c>) وتقاريرها (<c>purchase-receipt-reports</c>): مصادر سند الاستلام بكمياتها،
/// والمتابعة بموعد التوريد والرفض والإلغاء وسجل الأحداث. سندات الاستلام نفسها عبر <see cref="IDeliveryNoteService"/>.
/// لا ينشئ أي إجراء هنا حركة مخزون ولا قيداً ولا فاتورة.
/// </summary>
public interface IPurchaseReceiptTrackingService
{
    Task<PagedResult<PurchaseSupplyTrackingDto>> ListAsync(PurchaseSupplyTrackingQueryDto query, CancellationToken ct = default);
    Task<PurchaseSupplyTrackingDetailsDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>فتح متابعة لمصدر (أو إعادة فتحها بعد رفض/إلغاء)؛ القائمة النشطة تُعاد كما هي.</summary>
    Task<PurchaseSupplyTrackingDetailsDto> OpenAsync(OpenPurchaseSupplyTrackingDto request, CancellationToken ct = default);
    Task<PurchaseSupplyTrackingDetailsDto> UpdateAsync(Guid id, UpdatePurchaseSupplyTrackingDto request, CancellationToken ct = default);
    Task<PurchaseSupplyTrackingDetailsDto> RejectAsync(Guid id, ClosePurchaseSupplyTrackingDto request, CancellationToken ct = default);
    Task<PurchaseSupplyTrackingDetailsDto> CancelAsync(Guid id, ClosePurchaseSupplyTrackingDto request, CancellationToken ct = default);
    Task<List<PurchaseSupplyEventDto>> GetEventsAsync(Guid id, CancellationToken ct = default);
    /// <summary>فاتورة مشتريات مرحّلة كمصدر سند استلام بكميات كل سطر.</summary>
    Task<PurchaseReceiptSourceDto> GetPurchaseInvoiceSourceAsync(Guid invoiceId, CancellationToken ct = default);
    /// <summary>اتفاقية شراء سارية كمصدر سند استلام بكميات كل بند.</summary>
    Task<PurchaseReceiptSourceDto> GetContractSourceAsync(Guid contractId, CancellationToken ct = default);
    Task<PurchaseReceiptReportDto> ReportAsync(PurchaseSupplyTrackingQueryDto query, CancellationToken ct = default);
}
