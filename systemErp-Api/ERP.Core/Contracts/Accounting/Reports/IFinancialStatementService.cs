using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>القوائم المالية الرسمية من سطور القيود المرحّلة، بفترات محدَّدة ومقارنة اختيارية بفترة أخرى.</summary>
public interface IFinancialStatementService
{
    /// <summary>قائمة الدخل؛ مرشّح مركز التكلفة (وفروعه) يجعلها قائمة دخل المركز.</summary>
    Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, DateTime? compareFrom, DateTime? compareTo, string? module = null,
        Guid? costCenterId = null, CancellationToken ct = default);
    Task<FinancialPositionDto> GetFinancialPositionAsync(DateTime? asOf, DateTime? compareAsOf, string? module = null, CancellationToken ct = default);
    Task<CashFlowDto> GetCashFlowAsync(DateTime from, DateTime to, DateTime? compareFrom, DateTime? compareTo, string? module = null, CancellationToken ct = default);
    /// <summary>إيرادات مراكز التكلفة وتكاليفها وصافيها في فترة (ليس ميزان مراجعة).</summary>
    Task<CostCenterPerformanceDto> GetCostCenterPerformanceAsync(DateTime from, DateTime to, Guid? costCenterId = null, string? module = null, CancellationToken ct = default);
}
