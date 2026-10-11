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
        [FromQuery] DateTime? compareFrom, [FromQuery] DateTime? compareTo, CancellationToken ct)
        => Success(await _statements.GetIncomeStatementAsync(from, to, compareFrom, compareTo, ct));

    [HttpGet("FinancialPosition")]
    public async Task<IActionResult> FinancialPosition([FromQuery] DateTime? asOf, [FromQuery] DateTime? compareAsOf, CancellationToken ct)
        => Success(await _statements.GetFinancialPositionAsync(asOf, compareAsOf, ct));

    [HttpGet("CashFlow")]
    public async Task<IActionResult> CashFlow([FromQuery] DateTime from, [FromQuery] DateTime to,
        [FromQuery] DateTime? compareFrom, [FromQuery] DateTime? compareTo, CancellationToken ct)
        => Success(await _statements.GetCashFlowAsync(from, to, compareFrom, compareTo, ct));
}
