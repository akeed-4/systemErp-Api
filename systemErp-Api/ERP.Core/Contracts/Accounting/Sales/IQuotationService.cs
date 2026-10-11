using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IQuotationService : ICrudService<QuotationDto, CreateQuotationDto, UpdateQuotationDto>
{
    Task<InvoiceDto> ConvertToInvoiceAsync(Guid id, CancellationToken ct = default);
    Task<CommercialOrderDto> ConvertToOrderAsync(Guid id, CancellationToken ct = default);
}
