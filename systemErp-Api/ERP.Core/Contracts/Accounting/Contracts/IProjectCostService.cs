using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>تكلفة مشروع المقاولات وربحيته من القيود المرحّلة على مركز تكلفته، مقابل المخطط، مع التوقع عند الاكتمال.</summary>
public interface IProjectCostService
{
    Task<ProjectCostReportDto> ReportAsync(Guid contractId, CancellationToken ct = default);
}
