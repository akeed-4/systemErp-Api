using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

public interface IDashboardService
{
    /// <summary>أعداد وإجماليات لوحة التحكم؛ includeShowroom يضيف مؤشرات المعارض.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(bool includeShowroom, CancellationToken ct = default);
}
