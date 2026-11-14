using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

/// <summary>
/// بوابة الموظف: الحضور الميداني بالموقع. لا تتطلب صلاحية شاشة؛ الموظف من جلسة الدخول والمشروع من إسناده، والطلب لا يحمل
/// معرّف موظف. الموقع يُرسل عند الحضور والانصراف فقط (لا تتبّع).
/// </summary>
[Route("api/v1/me/attendance"), EmployeeSelfService, RequireModule(PlatformModules.Hr)]
public class MyAttendanceController : ErpControllerBase
{
    private readonly IFieldAttendanceService _field;
    public MyAttendanceController(IFieldAttendanceService field) => _field = field;

    /// <summary>اليوم: المشاريع المسندة، وسجل اليوم، وإمكان الحضور أو الانصراف.</summary>
    [HttpGet]
    public async Task<IActionResult> Today(CancellationToken ct) => Success(await _field.TodayAsync(ct));

    /// <summary>من خارج النطاق (لمشروع يقبل الاستثناء) يلزم <c>exceptionReason</c>، ويُسجَّل بانتظار مراجعة المشرف.</summary>
    [HttpPost("check-in")]
    public async Task<IActionResult> CheckIn([FromBody] GpsPunchRequestDto dto, CancellationToken ct)
    {
        var today = await _field.CheckInAsync(dto, ct);
        return Success(today, Pending(today) ? Messages.FieldAttendanceRecordedAsException : Messages.FieldAttendanceCheckedIn);
    }

    [HttpPost("check-out")]
    public async Task<IActionResult> CheckOut([FromBody] GpsPunchRequestDto dto, CancellationToken ct)
    {
        var today = await _field.CheckOutAsync(dto, ct);
        return Success(today, Pending(today) ? Messages.FieldAttendanceRecordedAsException : Messages.FieldAttendanceCheckedOut);
    }

    /// <summary>آخر تسجيل (هذا الطلب) استثناء بانتظار المراجعة.</summary>
    private static bool Pending(MyFieldAttendanceDto today)
        => today.Record?.Punches.LastOrDefault()?.ReviewStatus == GpsExceptionReviews.Pending;
}
