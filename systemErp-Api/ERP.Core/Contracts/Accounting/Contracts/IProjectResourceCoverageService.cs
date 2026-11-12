using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>تغطية احتياجات العمالة والمعدات في خطة التنفيذ بالموارد الفعلية المسندة (للقراءة).</summary>
public interface IProjectResourceCoverageService
{
    Task<ProjectResourceCoverageDto> GetAsync(Guid contractId, CancellationToken ct = default);
}
