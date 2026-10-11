using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Core.Contracts.Accounting;

public interface IInventoryReportService
{
    Task<LoadResult> LoadInventoryAuditAsync(ReportQueryDto query, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadItemMovementsAsync(ReportQueryDto query, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadItemLedgerAsync(ReportQueryDto query, DataSourceLoadOptions options, CancellationToken ct = default);
    Task<LoadResult> LoadCommercialTradeAsync(ReportQueryDto query, DataSourceLoadOptions options, CancellationToken ct = default);
}
