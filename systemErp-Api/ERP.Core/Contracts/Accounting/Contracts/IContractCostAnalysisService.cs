using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// تحليل تكلفة جدول الكميات من مكتبة التكاليف مجمّداً على الدراسة: القراءة، ومعاينة التسعير بالفروق، والتطبيق الصريح، والتعديل
/// اليدوي بسبب. تغيّر المكتبة لا يغيّر التحليل المجمّد.
/// </summary>
public interface IContractCostAnalysisService
{
    Task<ContractCostAnalysisDto> GetAsync(Guid contractId, CancellationToken ct = default);

    Task<ContractCostRefreshPreviewDto> PreviewAsync(Guid contractId, DateTime? pricingDate, CancellationToken ct = default);

    Task<ContractCostAnalysisDto> ApplyAsync(Guid contractId, ApplyContractCostRefreshDto request, CancellationToken ct = default);

    Task<ContractCostAnalysisDto> OverrideAsync(Guid contractId, Guid rowId, OverrideContractCostDto request, CancellationToken ct = default);
}
