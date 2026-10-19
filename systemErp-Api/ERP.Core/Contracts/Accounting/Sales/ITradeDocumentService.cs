using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// مستند تجارة عامة بجدوله المستقل (مبيعات، مشتريات، مرتجع مبيعات، مرتجع مشتريات). النوع ثابت لكل خدمة،
/// وأي نوع آخر في الطلب يُستبدل بنوعها. فواتير المعارض لها IInvoiceService وجدولها.
/// </summary>
public interface ITradeDocumentService
{
    InvoiceKind Kind { get; }
    Task<PagedResult<InvoiceDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>ينشئ المستند ويحسب الضريبة والإجماليات، ويرحّل القيد والمخزون إن لم يكن مسودة — في معاملة واحدة.</summary>
    Task<InvoiceDto> CreateAsync(CreateInvoiceDto request, CancellationToken ct = default);
    /// <summary>تعديل المسودة، أو المرحّل قبل إرساله للهيئة (يُعكس أثره ويُعاد ترحيله بنفس رقمه).</summary>
    Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto request, CancellationToken ct = default);
    Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceJournalDto> PreviewJournalAsync(CreateInvoiceDto request, CancellationToken ct = default);
    Task<InvoiceJournalDto> GetJournalAsync(Guid id, CancellationToken ct = default);
    Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default);
}

/// <summary>فواتير المبيعات (جدول SalesInvoice)، ومنها فواتير نقاط البيع.</summary>
public interface ISalesInvoiceService : ITradeDocumentService { }

/// <summary>فواتير المشتريات (جدول PurchaseInvoice).</summary>
public interface IPurchaseInvoiceService : ITradeDocumentService { }

/// <summary>مرتجع لفاتورة مرحّلة من جدول أصله.</summary>
public interface ITradeReturnService : ITradeDocumentService
{
    /// <summary>مرتجع (إشعار دائن/مدين) كلي أو جزئي لفاتورة مرحّلة: الطريقة الوحيدة لإلغاء أثر فاتورة صادرة.</summary>
    Task<InvoiceDto> CreateFromInvoiceAsync(CreateReturnInvoiceRequestDto request, CancellationToken ct = default);
}

/// <summary>مرتجعات المبيعات (جدول SalesReturn) على فواتير المبيعات.</summary>
public interface ISalesReturnService : ITradeReturnService { }

/// <summary>مرتجعات المشتريات (جدول PurchaseReturn) على فواتير المشتريات.</summary>
public interface IPurchaseReturnService : ITradeReturnService { }
