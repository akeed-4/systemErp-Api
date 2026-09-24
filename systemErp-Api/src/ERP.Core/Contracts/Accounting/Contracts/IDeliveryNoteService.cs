using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IDeliveryNoteService
{
    Task<PagedResult<DeliveryNoteDto>> ListAsync(PaginationParams query, CancellationToken ct = default);
    Task<DeliveryNoteDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<DeliveryNoteDto> CreateAsync(CreateDeliveryNoteDto request, CancellationToken ct = default);
    /// <summary>تعديل بيان لم يُفوتر ولا مرتجعات عليه.</summary>
    Task<DeliveryNoteDto> UpdateAsync(Guid id, UpdateDeliveryNoteDto request, CancellationToken ct = default);
    /// <summary>حذف بيان لم يُفوتر ولا مرتجعات عليه.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<DeliveryReturnNoteDto> GetReturnAsync(Guid id, CancellationToken ct = default);
    /// <summary>حذف مرتجع: تُعاد الكميات المرتجعة إلى البيان الأصلي.</summary>
    Task DeleteReturnAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<DeliveryReturnNoteDto>> ListReturnsAsync(PaginationParams query, CancellationToken ct = default);
    /// <summary>مرتجع من بيان تسليم: يحدّث الكميات المرتجعة وحالة البيان.</summary>
    Task<DeliveryReturnNoteDto> CreateReturnAsync(CreateDeliveryReturnNoteDto request, CancellationToken ct = default);
    /// <summary>فاتورة مرحلية من بيان تسليم: تفوتر مستخلص العقد المحدد وتربط الفاتورة بالبيان.</summary>
    Task<InvoiceDto> CreateMilestoneInvoiceAsync(Guid deliveryNoteId, Guid milestoneId, CancellationToken ct = default);
}
