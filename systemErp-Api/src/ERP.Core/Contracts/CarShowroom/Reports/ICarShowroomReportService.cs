using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Core.Contracts.CarShowroom;

public interface ICarShowroomReportService
{
    Task<List<CarSalesPerformanceRowDto>> GetSalesPerformanceAsync(CarReportQueryDto q, CancellationToken ct = default);
    Task<List<CarVinInventoryRowDto>> GetVinInventoryAsync(CancellationToken ct = default);
    Task<List<CarZatcaMarginTaxRowDto>> GetZatcaMarginTaxAsync(CarReportQueryDto q, CancellationToken ct = default);
    Task<List<CarProcurementTrackingRowDto>> GetProcurementTrackingAsync(CarReportQueryDto q, CancellationToken ct = default);
    Task<List<CarProfitLossRowDto>> GetProfitLossAsync(CarReportQueryDto q, CancellationToken ct = default);
    Task<List<CarInstallmentReceivableRowDto>> GetInstallmentsReceivableAsync(CancellationToken ct = default);
    Task<List<CarSupplierProcurementRowDto>> GetSuppliersProcurementAsync(CarReportQueryDto q, CancellationToken ct = default);
    Task<List<CarDailyMonthlySalesRowDto>> GetDailyMonthlySalesAsync(CarReportQueryDto q, CancellationToken ct = default);
    Task<LoadResult> LoadSalesPerformanceAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadVinInventoryAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadZatcaMarginTaxAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadProcurementTrackingAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadProfitLossAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadInstallmentsReceivableAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadSuppliersProcurementAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadDailyMonthlySalesAsync(CarReportQueryDto q, DataSourceLoadOptions options, CancellationToken ct = default);
}
