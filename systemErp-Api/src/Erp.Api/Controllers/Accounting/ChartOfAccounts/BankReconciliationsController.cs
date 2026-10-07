using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>التسوية البنكية: ورقة العمل، الاعتماد، السجل، وإلغاء آخر تسوية.</summary>
[Route("api/v1/bankreconciliations"), RequireScreen("accounts")]
public class BankReconciliationsController : ErpControllerBase
{
    private readonly IBankReconciliationService _reconciliations;
    public BankReconciliationsController(IBankReconciliationService reconciliations) => _reconciliations = reconciliations;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? accountCode, CancellationToken ct) => Success(await _reconciliations.ListAsync(accountCode, ct));

    /// <summary>حركات الحساب غير المطابَقة حتى تاريخ الكشف ورصيد الدفاتر.</summary>
    [HttpGet("Worksheet")]
    public async Task<IActionResult> Worksheet([FromQuery] string accountCode, [FromQuery] DateTime statementDate, CancellationToken ct)
        => Success(await _reconciliations.GetWorksheetAsync(accountCode, statementDate, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBankReconciliationDto request, CancellationToken ct)
        => Success(await _reconciliations.CreateAsync(request, ct), Messages.BankReconciliationSaved);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _reconciliations.DeleteAsync(id, ct);
        return Success(Messages.BankReconciliationDeleted);
    }
}
