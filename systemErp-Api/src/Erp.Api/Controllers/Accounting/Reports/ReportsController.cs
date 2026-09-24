using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// التقارير القائمية تقبل خيارات DevExtreme (filter/sort/group/skip/take/totalSummary/groupSummary/requireTotalCount)
/// وتُعيد <c>LoadResult</c> مباشرة ({ data, totalCount, summary, groupCount }) كما يتوقعها CustomStore، دون مغلّف ApiResponse.
/// التقارير المفردة (KPIs) تبقى بمغلّف ApiResponse.
/// </summary>
[Route("api/v1/reports")]
public class ReportsController : ErpControllerBase
{
    private readonly IAccountingReportService _accounting;
    private readonly IInventoryReportService _inventory;

    public ReportsController(IAccountingReportService accounting, IInventoryReportService inventory)
    {
        _accounting = accounting; _inventory = inventory;
    }

    // ---------- تقارير مفردة (KPIs) ----------
    /// <summary>مؤشرات لوحة القيادة (FinancialStats).</summary>
    [HttpGet("financial-stats"), RequireScreen("dashboard")]
    public async Task<IActionResult> Stats(CancellationToken ct) => Success(await _accounting.GetFinancialStatsAsync(ct));

    [HttpGet("financial-summary"), RequireScreen("reports")]
    public async Task<IActionResult> Summary(CancellationToken ct) => Success(await _accounting.GetFinancialSummaryAsync(ct));

    [HttpGet("vat-return"), RequireScreen("reports")]
    public async Task<IActionResult> Vat([FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        => Success(await _accounting.GetVatReturnAsync(from, to, ct));

    /// <summary>رأس كشف الحساب (الرصيد الافتتاحي والختامي) مع الحركات كاملة.</summary>
    [HttpGet("account-statement/{code}"), RequireScreen("reports")]
    public async Task<IActionResult> Statement(string code, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Success(await _accounting.GetAccountStatementAsync(code, from, to, ct));

    // ---------- تقارير قائمية (DevExtreme loadOptions) ----------
    [HttpGet("trial-balance"), RequireScreen("reports")]
    public async Task<IActionResult> TrialBalance([FromQuery] DateTime? from, [FromQuery] DateTime? to, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _accounting.LoadTrialBalanceAsync(from, to, loadOptions, ct));

    [HttpGet("account-statement/{code}/entries"), RequireScreen("reports")]
    public async Task<IActionResult> StatementEntries(string code, [FromQuery] DateTime? from, [FromQuery] DateTime? to, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _accounting.LoadAccountStatementEntriesAsync(code, from, to, loadOptions, ct));

    [HttpGet("inventory-audit"), RequireScreen("reports")]
    public async Task<IActionResult> Audit([FromQuery] ReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _inventory.LoadInventoryAuditAsync(q, loadOptions, ct));

    [HttpGet("item-movements"), RequireScreen("reports")]
    public async Task<IActionResult> Movements([FromQuery] ReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _inventory.LoadItemMovementsAsync(q, loadOptions, ct));

    [HttpGet("item-ledger"), RequireScreen("reports")]
    public async Task<IActionResult> Ledger([FromQuery] ReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _inventory.LoadItemLedgerAsync(q, loadOptions, ct));

    [HttpGet("trade-commercial"), RequireScreen("reports")]
    public async Task<IActionResult> Trade([FromQuery] ReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _inventory.LoadCommercialTradeAsync(q, loadOptions, ct));
}
