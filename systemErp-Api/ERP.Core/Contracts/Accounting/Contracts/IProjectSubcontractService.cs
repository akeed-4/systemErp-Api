using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>مقاولو الباطن لمشروع المقاولات (عقد العميل): عقود الموردين المرتبطة به وتغطية بنوده. نطاق السجل يفحصه المتحكم.</summary>
public interface IProjectSubcontractService
{
    Task<ProjectSubcontractsDto> GetAsync(Guid contractId, CancellationToken ct = default);
}
