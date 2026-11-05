using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>أنواع العقود للمنشأة: المسار وطريقة الفوترة وحد التبسيط. الرمز ثابت، والنوع المستخدم لا يُحذف ولا تتغير فوترته.</summary>
public interface IContractTypeService
{
    Task<List<ContractTypeDto>> ListAsync(bool activeOnly, CancellationToken ct = default);
    Task<ContractTypeDto> CreateAsync(SaveContractTypeDto request, CancellationToken ct = default);
    Task<ContractTypeDto> UpdateAsync(Guid id, SaveContractTypeDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// كتالوج دورة العقود العام (لكل المنشآت، يديره مشرف المنصة): نصوص الحالات ومددها وإجراءاتها، والانتقالات وصلاحياتها وشروطها.
/// معرّفات الحالات وترتيبها ثابتة لأن قواعد العمل تعتمد عليها.
/// </summary>
public interface IContractWorkflowCatalogService
{
    Task<ContractWorkflowCatalogDto> GetAsync(CancellationToken ct = default);
    Task<ContractStageAdminDto> UpdateStageAsync(string stageId, SaveContractStageDto request, CancellationToken ct = default);
    Task<ContractTransitionAdminDto> CreateTransitionAsync(SaveContractTransitionDto request, CancellationToken ct = default);
    Task<ContractTransitionAdminDto> UpdateTransitionAsync(int transitionId, SaveContractTransitionDto request, CancellationToken ct = default);
}
