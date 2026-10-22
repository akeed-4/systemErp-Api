using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>القوائم المالية الرسمية بفترات ومقارنات: الدخل، المركز المالي، التدفقات النقدية.</summary>
[Route("api/v1/financialstatements"), RequireScreen("reports")]
public class FinancialStatementsController : ErpControllerBase
{
    private readonly IFinancialStatementService _statements;
    public FinancialStatementsController(IFinancialStatementService statements) => _statements = statements;

    [HttpGet("IncomeStatement")]
    public async Task<IActionResult> IncomeStatement([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] DateTime? compareFrom, [FromQuery] DateTime? compareTo, [FromQuery] string? module, [FromQuery] Guid? costCenterId, CancellationToken ct)
        => Success(await _statements.GetIncomeStatementAsync(from, to, compareFrom, compareTo, module, costCenterId, ct));

    /// <summary>أداء مراكز التكلفة: إيراد كل مركز وتكلفته وصافيه (المرشّح يشمل الفروع).</summary>
    [HttpGet("CostCenterPerformance")]
    public async Task<IActionResult> CostCenterPerformance([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] Guid? costCenterId,
        [FromQuery] string? module, CancellationToken ct)
        => Success(await _statements.GetCostCenterPerformanceAsync(from, to, costCenterId, module, ct));

    [HttpGet("FinancialPosition")]
    public async Task<IActionResult> FinancialPosition([FromQuery] DateTime? asOf, [FromQuery] DateTime? compareAsOf, [FromQuery] string? module, CancellationToken ct)
        => Success(await _statements.GetFinancialPositionAsync(asOf, compareAsOf, module, ct));

    [HttpGet("CashFlow")]
    public async Task<IActionResult> CashFlow([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] DateTime? compareFrom, [FromQuery] DateTime? compareTo, [FromQuery] string? module, CancellationToken ct)
        => Success(await _statements.GetCashFlowAsync(from, to, compareFrom, compareTo, module, ct));
}
