using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface ICommercialOrderService : ICrudService<CommercialOrderDto, CreateCommercialOrderDto, UpdateCommercialOrderDto>
{
    Task<InvoiceDto> ConvertToInvoiceAsync(Guid id, CancellationToken ct = default);
}
