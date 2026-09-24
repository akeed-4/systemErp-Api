using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

public interface ICommercialContractService : ICrudService<CommercialContractDto, CreateCommercialContractDto, UpdateCommercialContractDto>
{
    Task<CommercialContractDto> AdvanceStageAsync(Guid id, AdvanceStageRequestDto request, CancellationToken ct = default);
    /// <summary>يفوتر مستخلصاً: فاتورة ضريبية + قيد (مدينون / إيراد / ضريبة مخرجات) + تحديث إجماليات العقد.</summary>
    Task<InvoiceDto> BillMilestoneAsync(Guid contractId, Guid milestoneId, CancellationToken ct = default);
}
