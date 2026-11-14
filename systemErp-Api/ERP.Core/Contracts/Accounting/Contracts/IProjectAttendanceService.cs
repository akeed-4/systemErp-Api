using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// حضور فريق مشروع المقاولات ومراجعة استثناءات خارج النطاق. نطاق السجل والصلاحية يفحصهما المتحكم؛ قرار الاستثناء يُكتب في سجل
/// الحضور عبر <see cref="IAttendanceService"/>.
/// </summary>
public interface IProjectAttendanceService
{
    /// <summary>حضور اليوم (فارغ = اليوم المحلي للمنشأة).</summary>
    Task<ProjectAttendanceDayDto> DayAsync(Guid contractId, DateTime? date, CancellationToken ct = default);
    Task<List<ProjectAttendanceExceptionDto>> PendingExceptionsAsync(Guid contractId, CancellationToken ct = default);
    Task<ProjectAttendanceDayDto> ReviewExceptionAsync(Guid contractId, Guid punchId, bool approve, GpsExceptionReviewDto review, CancellationToken ct = default);
}
