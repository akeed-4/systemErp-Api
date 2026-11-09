using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

public interface IDashboardService
{
    /// <summary>أعداد وإجماليات لوحة التحكم؛ includeShowroom يضيف مؤشرات المعارض.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(bool includeShowroom, CancellationToken ct = default);
    /// <summary>أداء المبيعات للفترة (week|month|year حتى اليوم) والموقع (null = كل المواقع المسموحة) مع المقارنة بالفترة السابقة.</summary>
    Task<SalesPerformanceDto> GetSalesPerformanceAsync(string? period, Guid? warehouseId, CancellationToken ct = default);
}
