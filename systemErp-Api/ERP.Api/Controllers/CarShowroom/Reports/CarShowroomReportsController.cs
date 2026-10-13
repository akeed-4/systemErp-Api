using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

/// <summary>كل تقارير معرض السيارات تقبل خيارات DevExtreme وتُعيد LoadResult مباشرة.</summary>
[Route("api/v1/reports/car")]
[RequireModule(PlatformModules.CarShowroom)]
[RequireScreen("reports")]
public class CarShowroomReportsController : ErpControllerBase
{
    private readonly ICarShowroomReportService _reports;
    private readonly ICostVisibility _cost;
    public CarShowroomReportsController(ICarShowroomReportService reports, ICostVisibility cost)
    {
        _reports = reports; _cost = cost;
    }

    /// <summary>تقارير تعرض تكلفة السيارات أو ربحها: تتطلب صلاحية رؤية التكلفة فوق صلاحية التقارير.</summary>
    private async Task RequireCostViewAsync(CancellationToken ct)
    {
        if (!await _cost.CanViewAsync(ct)) throw new ForbiddenException();
    }

    [HttpGet("SalesPerformance")]
    public async Task<IActionResult> SalesPerformance([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadSalesPerformanceAsync(q, loadOptions, ct));
    }

    [HttpGet("VinInventory")]
    public async Task<IActionResult> VinInventory(DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadVinInventoryAsync(loadOptions, ct));
    }

    [HttpGet("ZatcaMarginTax")]
    public async Task<IActionResult> ZatcaMargin([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadZatcaMarginTaxAsync(q, loadOptions, ct));
    }

    /// <summary>سيارات مباعة بهامش الربح يختلف هامشها وضريبتها بين طريقتي الأساس (لقرار المحاسب في إعداد أساس الهامش).</summary>
    [HttpGet("MarginBasisComparison")]
    public async Task<IActionResult> MarginBasisComparison([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadMarginBasisComparisonAsync(q, loadOptions, ct));
    }

    // ---- تقارير المصاريف والتكاليف الإضافية (كلها تعرض تكلفة: تتطلب صلاحية رؤية التكلفة) ----
    /// <summary>ربح كل سيارة مباعة بعد كل المصاريف.</summary>
    [HttpGet("VehicleNetProfit")]
    public async Task<IActionResult> VehicleNetProfit([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadVehicleNetProfitAsync(q, loadOptions, ct));
    }

    /// <summary>المصاريف المتوقعة مقابل الفعلية وفرقها.</summary>
    [HttpGet("EstimatedVsActual")]
    public async Task<IActionResult> EstimatedVsActual([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadEstimatedVsActualAsync(q, loadOptions, ct));
    }

    /// <summary>رسوم تم الشهرية حسب النوع.</summary>
    [HttpGet("TammMonthlyFees")]
    public async Task<IActionResult> TammMonthlyFees([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadTammMonthlyFeesAsync(q, loadOptions, ct));
    }

    /// <summary>مصاريف على سيارات لم تُشترَ (استعلامات قبل الشراء، وشراء أُلغي بعد الصرف).</summary>
    [HttpGet("UnpurchasedVehicleExpenses")]
    public async Task<IActionResult> UnpurchasedVehicleExpenses([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadUnpurchasedVehicleExpensesAsync(q, loadOptions, ct));
    }

    /// <summary>سيارات في الطريق وتكلفتها.</summary>
    [HttpGet("VehiclesInTransit")]
    public async Task<IActionResult> VehiclesInTransit(DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadVehiclesInTransitAsync(loadOptions, ct));
    }

    /// <summary>عمر المخزون مع تكلفته.</summary>
    [HttpGet("InventoryAging")]
    public async Task<IActionResult> InventoryAging(DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadInventoryAgingAsync(loadOptions, ct));
    }

    [HttpGet("ProcurementTracking")]
    public async Task<IActionResult> Procurement([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadProcurementTrackingAsync(q, loadOptions, ct));

    [HttpGet("ProfitLoss")]
    public async Task<IActionResult> ProfitLoss([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadProfitLossAsync(q, loadOptions, ct));
    }

    [HttpGet("InstallmentsReceivable")]
    public async Task<IActionResult> Installments(DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadInstallmentsReceivableAsync(loadOptions, ct));

    [HttpGet("SuppliersProcurement")]
    public async Task<IActionResult> Suppliers([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
        => Ok(await _reports.LoadSuppliersProcurementAsync(q, loadOptions, ct));

    [HttpGet("DailyMonthlySales")]
    public async Task<IActionResult> DailyMonthly([FromQuery] CarReportQueryDto q, DataSourceLoadOptions loadOptions, CancellationToken ct)
    {
        await RequireCostViewAsync(ct);
        return Ok(await _reports.LoadDailyMonthlySalesAsync(q, loadOptions, ct));
    }
}
