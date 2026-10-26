using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// دورة حالات العقد: كل انتقال يمر بدالة واحدة <see cref="MoveAsync"/> تتحقق من أن الانتقال معرّف في جدول الانتقالات،
/// وأن المستخدم يملك صلاحيته (ونطاقه وحد اعتماده)، وأن شروطه مستوفاة، ثم تسجّله في سجل الانتقالات.
/// </summary>
public interface IContractWorkflowService
{
    Task<ContractWorkflowDto> GetAsync(Guid contractId, CancellationToken ct = default);
    Task<ContractWorkflowDto> MoveAsync(Guid contractId, MoveContractRequestDto request, CancellationToken ct = default);
    /// <summary>قرار المراجع القانوني/المالي؛ اكتمال الموافقتين ينقل العقد للاعتماد، والإعادة ترجعه للمسودة.</summary>
    Task<ContractWorkflowDto> ReviewAsync(Guid contractId, ContractReviewRequestDto request, CancellationToken ct = default);
    Task<ContractWorkflowDto> SaveSigningAsync(Guid contractId, ContractSigningDto request, CancellationToken ct = default);

    Task<ContractAttachmentDto> AddAttachmentAsync(Guid contractId, string kind, string fileName, byte[] content, CancellationToken ct = default);
    Task<ContractFileDto> GetAttachmentAsync(Guid contractId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAttachmentAsync(Guid contractId, Guid attachmentId, CancellationToken ct = default);
}
