using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>القوائم المالية الرسمية من سطور القيود المرحّلة، بفترات محدَّدة ومقارنة اختيارية بفترة أخرى.</summary>
public interface IFinancialStatementService
{
    Task<IncomeStatementDto> GetIncomeStatementAsync(DateTime from, DateTime to, DateTime? compareFrom, DateTime? compareTo, CancellationToken ct = default);
    Task<FinancialPositionDto> GetFinancialPositionAsync(DateTime? asOf, DateTime? compareAsOf, CancellationToken ct = default);
    Task<CashFlowDto> GetCashFlowAsync(DateTime from, DateTime to, DateTime? compareFrom, DateTime? compareTo, CancellationToken ct = default);
}
