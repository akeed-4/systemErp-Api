using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface IMaterialRequisitionService : ICrudService<MaterialRequisitionDto, CreateMaterialRequisitionDto, UpdateMaterialRequisitionDto>
{
    Task<MaterialRequisitionDto> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDto> ConvertToPurchaseInvoiceAsync(Guid id, CancellationToken ct = default);
}
