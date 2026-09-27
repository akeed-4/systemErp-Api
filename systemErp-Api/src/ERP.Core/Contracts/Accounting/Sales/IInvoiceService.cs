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
    /// <summary>تعديل فاتورة (مسودة أو مرحّلة): المرحّلة يُعكس أثرها ثم تُرحَّل من جديد بنفس الرقم؛ status=draft يعيدها مسودة.</summary>
    Task<InvoiceDto> UpdateAsync(Guid id, UpdateInvoiceDto request, CancellationToken ct = default);
    /// <summary>حذف فاتورة أنشأتها وحدة أخرى بعد عكس أثرها؛ للاستدعاء من الوحدة المصدر فقط (مثل إلغاء عملية POS).</summary>
    Task DeleteSourceInvoiceAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDto> CreateReturnAsync(CreateReturnInvoiceRequestDto request, CancellationToken ct = default);
    /// <summary>يرحّل مسودة فاتورة: مخزون + قيد + QR.</summary>
    Task<InvoiceDto> PostDraftAsync(Guid id, CancellationToken ct = default);
    /// <summary>حذف فاتورة: المسودة مباشرة، والمرحّلة بعد عكس قيدها ومخزونها (مع بقاء قيد العكس للتدقيق). يُرفض ما أُرسل لهيئة الزكاة أو له مرتجعات.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default);
}
