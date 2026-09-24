using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.POS;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.POS;

public interface IPosSaleService
{
    /// <summary>يتمّ البيع كله في معاملة واحدة: تسعير وعروض وكوبون وولاء، فاتورة + مخزون + قيد، ثم تحديث الوردية والولاء.</summary>
    Task<PosTransactionDto> CheckoutAsync(CheckoutRequestDto request, CancellationToken ct = default);
    Task<PagedResult<PosTransactionDto>> ListAsync(Guid? shiftId, PaginationParams query, CancellationToken ct = default);
    Task<PosTransactionDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<PosTransactionDto> GetByInvoiceNumberAsync(string invoiceNumber, CancellationToken ct = default);
    Task<PosTransactionDto> UpdateAsync(Guid id, UpdatePosTransactionRequestDto request, CancellationToken ct = default);
    /// <summary>إلغاء عملية: يُعكس القيد والمخزون وتُحذف فاتورتها وتُعاد أرقام الوردية والكوبون والولاء؛ يبقى السجل بحالة voided.</summary>
    Task<PosTransactionDto> VoidAsync(Guid id, CancellationToken ct = default);
    /// <summary>حذف عملية نهائياً بعد عكس أثرها.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ZatcaSubmitResultDto> SubmitToZatcaAsync(Guid id, CancellationToken ct = default);
}
