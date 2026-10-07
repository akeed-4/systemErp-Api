using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IInvoiceService
{
    /// <summary>hasVehicleLines: true = فواتير السيارات متعددة الأسطر فقط، false = بدونها، null = الكل.</summary>
    Task<PagedResult<InvoiceDto>> ListAsync(InvoiceKind? kind, PaginationParams query, bool? hasVehicleLines = null, CancellationToken ct = default);
    Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>ينشئ فاتورة (بيع/شراء) ويحسب الضريبة والإجماليات ويرحّل القيد والمخزون ويولّد QR — كلها في معاملة واحدة.</summary>
    Task<InvoiceDto> CreateAsync(CreateInvoiceDto request, CancellationToken ct = default);
    /// <summary>تعديل مسودة فقط (وترحيلها إن أُرسلت status=posted). الفاتورة المرحّلة مستند صادر مقفل: تُصحَّح بمرتجع.</summary>
    Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto request, CancellationToken ct = default);
    /// <summary>مرتجع (إشعار دائن/مدين) كلي أو جزئي لفاتورة مرحّلة: الطريقة الوحيدة لإلغاء أثر فاتورة صادرة.</summary>
    Task<InvoiceDto> CreateReturnAsync(CreateReturnInvoiceRequestDto request, CancellationToken ct = default);
    /// <summary>يرحّل مسودة فاتورة: مخزون + قيد + QR.</summary>
    Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default);
    /// <summary>حذف مسودة فقط (رقمها مؤقت فلا فجوة في التسلسل). الفاتورة المرحّلة لا تُحذف.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default);
    /// <summary>معاينة القيد (مخزون/تكلفة إيرادات/ضريبة/دفع) الذي سيُنشأ عند ترحيل هذه الفاتورة — بلا أي أثر محفوظ.</summary>
    Task<InvoiceJournalDto> PreviewJournalAsync(CreateInvoiceDto request, CancellationToken ct = default);
    /// <summary>القيد الفعلي لفاتورة مرحّلة.</summary>
    Task<InvoiceJournalDto> GetJournalAsync(Guid id, CancellationToken ct = default);
}
