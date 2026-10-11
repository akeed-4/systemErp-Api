using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IApprovalPolicyService : ICrudService<ApprovalPolicyDto, CreateApprovalPolicyDto, UpdateApprovalPolicyDto>
{
    Task<ApprovalPolicyDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
}
