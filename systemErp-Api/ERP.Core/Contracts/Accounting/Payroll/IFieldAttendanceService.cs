using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// الحضور الميداني للموظف من الجوال: الموظف من جلسة الدخول، والمشروع من إسناده (مورد المشروع)، والموقع يتحقق منه الخادم
/// مقابل نطاق المشروع ودقة القراءة، ثم يُكتب في سجل الحضور اليومي نفسه عبر <see cref="IAttendanceService"/>.
/// </summary>
public interface IFieldAttendanceService
{
    Task<MyFieldAttendanceDto> TodayAsync(CancellationToken ct = default);
    Task<MyFieldAttendanceDto> CheckInAsync(GpsPunchRequestDto request, CancellationToken ct = default);
    Task<MyFieldAttendanceDto> CheckOutAsync(GpsPunchRequestDto request, CancellationToken ct = default);
}
