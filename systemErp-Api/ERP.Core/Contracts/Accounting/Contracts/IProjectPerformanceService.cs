using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// تحليل أداء مشاريع المقاولات للإدارة في فترة (للقراءة فقط، لا يخزن شيئاً): في نطاق وصول المستخدم إلى العقود، ومشروع واحد
/// أو مرحلة منه اختيارياً.
/// </summary>
public interface IProjectPerformanceService
{
    Task<ProjectPerformanceDto> AnalyzeAsync(DateTime? from, DateTime? to, Guid? contractId, Guid? phaseId, CancellationToken ct = default);
}
