using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarShowroomReportService
{
    Task<LoadResult> LoadSalesPerformanceAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadVinInventoryAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadZatcaMarginTaxAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    // تقارير المصاريف والتكاليف الإضافية
    Task<LoadResult> LoadVehicleNetProfitAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadEstimatedVsActualAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadTammMonthlyFeesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadUnpurchasedVehicleExpensesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadVehiclesInTransitAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadInventoryAgingAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    /// <summary>سيارات مباعة بهامش الربح يختلف هامشها بين أساس سعر الشراء وأساس التكلفة الكلية.</summary>
    Task<LoadResult> LoadMarginBasisComparisonAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadProcurementTrackingAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadProfitLossAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadInstallmentsReceivableAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadSuppliersProcurementAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadDailyMonthlySalesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
}
