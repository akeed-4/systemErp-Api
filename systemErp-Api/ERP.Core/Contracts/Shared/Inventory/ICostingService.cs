using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICostingService
{
    Task<CostingPolicyDto> GetPolicyAsync(CancellationToken ct = default);
    Task<CostingPolicyDto> SetPolicyAsync(CreateCostingPolicyDto policy, CancellationToken ct = default);
    /// <summary>يعيد احتساب متوسط تكلفة كل الأصناف من حركات المخزون الفعلية وفق الطريقة الحالية.</summary>
    Task<RecalculateCostingResultDto> RecalculateAllAsync(CancellationToken ct = default);
}
