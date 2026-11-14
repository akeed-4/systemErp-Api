using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// حضور فريق مشروع المقاولات بالموقع ومراجعة استثناءات خارج النطاق. المسار تحت سجل العقد فيخضع لنطاق الوصول على العقد
/// (المشرف بنطاق «المسندة إليه» لا يرى إلا مشاريعه)، والشاشة «حضور فريق المشروع» مستقلة لأنها بيانات موقع شخصية.
/// </summary>
[Route("api/v1/commercialcontracts/{id:guid}/project/attendance")]
[RequireModule(PlatformModules.Construction)]
[ScopedRecord(ScopedRecordTypes.CommercialContractKey)]
[RequireFeature(PlanFeatures.CommercialCycle)]
public class ProjectAttendanceController : ErpControllerBase
{
    private const string Screen = PermissionCatalog.ProjectAttendanceScreenId;
    private readonly IProjectAttendanceService _attendance;

    public ProjectAttendanceController(IProjectAttendanceService attendance) => _attendance = attendance;

    /// <summary>حضور الفريق في يوم (date فارغ = اليوم المحلي للمنشأة).</summary>
    [HttpGet, RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Day(Guid id, [FromQuery] DateTime? date, CancellationToken ct) => Success(await _attendance.DayAsync(id, date, ct));

    /// <summary>استثناءات خارج النطاق بانتظار المراجعة (كل الأيام).</summary>
    [HttpGet("exceptions"), RequireScreen(Screen, ScreenAction.View)]
    public async Task<IActionResult> Exceptions(Guid id, CancellationToken ct) => Success(await _attendance.PendingExceptionsAsync(id, ct));

    [HttpPost("punches/{punchId:guid}/approve"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Approve(Guid id, Guid punchId, [FromBody] GpsExceptionReviewDto dto, CancellationToken ct)
        => Success(await _attendance.ReviewExceptionAsync(id, punchId, approve: true, dto, ct), Messages.AttendanceExceptionApproved);

    /// <summary>الرفض بملاحظة إلزامية.</summary>
    [HttpPost("punches/{punchId:guid}/reject"), RequireScreen(Screen, ScreenAction.Approve)]
    public async Task<IActionResult> Reject(Guid id, Guid punchId, [FromBody] GpsExceptionReviewDto dto, CancellationToken ct)
        => Success(await _attendance.ReviewExceptionAsync(id, punchId, approve: false, dto, ct), Messages.AttendanceExceptionRejected);
}
