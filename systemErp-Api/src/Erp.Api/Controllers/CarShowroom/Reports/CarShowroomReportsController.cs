using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>كل تقارير معرض السيارات تقبل خيارات DevExtreme وتُعيد LoadResult مباشرة.</summary>
[Route("api/v1/reports/car")]
[RequireScreen("reports")]
public class CarShowroomReportsController : ErpControllerBase
{
    private readonly ICarShowroomReportService _reports;
    public CarShowroomReportsController(ICarShowroomReportService reports) => _reports = reports;

    [HttpGet("sales-performance")]
    public async Task<IActionResult> SalesPerformance([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadSalesPerformanceAsync(q, loadOptions, ct));

    [HttpGet("vin-inventory")]
    public async Task<IActionResult> VinInventory(DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadVinInventoryAsync(loadOptions, ct));

    [HttpGet("zatca-margin-tax")]
    public async Task<IActionResult> ZatcaMargin([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadZatcaMarginTaxAsync(q, loadOptions, ct));

    [HttpGet("procurement-tracking")]
    public async Task<IActionResult> Procurement([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadProcurementTrackingAsync(q, loadOptions, ct));

    [HttpGet("profit-loss")]
    public async Task<IActionResult> ProfitLoss([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadProfitLossAsync(q, loadOptions, ct));

    [HttpGet("installments-receivable")]
    public async Task<IActionResult> Installments(DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadInstallmentsReceivableAsync(loadOptions, ct));

    [HttpGet("suppliers-procurement")]
    public async Task<IActionResult> Suppliers([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadSuppliersProcurementAsync(q, loadOptions, ct));

    [HttpGet("daily-monthly-sales")]
    public async Task<IActionResult> DailyMonthly([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadDailyMonthlySalesAsync(q, loadOptions, ct));
}
