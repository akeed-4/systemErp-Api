using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// حضور فريق المشروع لمدير المشروع ومشرفه: المسندون (مورد نشط بنوع عمالة أو مهندس مربوط بموظف) ومن سجّل على المشروع،
/// بحالاتهم وأوقاتهم وتحقق مواقعهم، واستثناءات خارج النطاق ومراجعتها. القراءة من سجل الحضور اليومي، والقرار يكتبه
/// <see cref="IAttendanceService"/>. سجل الموظف على مشروع آخر لا تُعرض تفاصيله، والإحداثيات لا تُعرض إلا لاستثناء.
/// </summary>
public class ProjectAttendanceService : IProjectAttendanceService
{
    private static readonly string[] FieldKinds = { ProjectResourceKinds.Labor, ProjectResourceKinds.Engineer };

    private readonly ErpDbContext _db;
    private readonly IAttendanceService _attendance;
    private readonly TimeProvider _time;

    public ProjectAttendanceService(ErpDbContext db, IAttendanceService attendance, TimeProvider time)
    {
        _db = db; _attendance = attendance; _time = time;
    }

    public async Task<ProjectAttendanceDayDto> DayAsync(Guid contractId, DateTime? date, CancellationToken ct = default)
    {
        await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, ct);
        var day = date?.Date ?? TenantTimeZone.ToLocal(_time.GetUtcNow().UtcDateTime, await TenantTimeZone.ForCurrentTenantAsync(_db, ct)).Date;
        var profile = await _db.Set<ConstructionProjectProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, ct);

        var assigned = (await _db.Set<ProjectResource>().AsNoTracking()
                .Where(r => r.ContractId == contractId && r.IsActive && r.EmployeeId != null && FieldKinds.Contains(r.Kind))
                .Select(r => new { EmployeeId = r.EmployeeId!.Value, r.Name, r.Kind }).ToListAsync(ct))
            .GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.First());
        var assignedIds = assigned.Keys.ToList();
        var records = await _db.Set<AttendanceRecord>().AsNoTracking().Include(r => r.Punches)
            .Where(r => r.Date == day && (assignedIds.Contains(r.EmployeeId) || r.ContractId == contractId))
            .ToDictionaryAsync(r => r.EmployeeId, ct);
        var ids = assignedIds.Union(records.Keys).ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var onLeave = (await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(l => ids.Contains(l.EmployeeId) && l.Status == LeaveStatuses.Approved && l.StartDate <= day && l.EndDate >= day)
            .Select(l => l.EmployeeId).ToListAsync(ct)).ToHashSet();

        var rows = ids.Where(employees.ContainsKey).Select(id =>
        {
            var e = employees[id];
            var r = records.GetValueOrDefault(id);
            var resource = assigned.GetValueOrDefault(id);
            var elsewhere = r?.ContractId is Guid other && other != contractId;
            var row = new ProjectAttendanceRowDto
            {
                EmployeeId = id, EmployeeCode = e.Code, EmployeeName = e.NameAr, EmployeeNameEn = e.NameEn,
                ResourceName = resource?.Name, ResourceKind = resource?.Kind, Assigned = resource != null, OnApprovedLeave = onLeave.Contains(id),
                OnOtherProject = elsewhere,
            };
            if (r == null || elsewhere) return row;
            row.Status = r.Status; row.Source = r.Source; row.CheckIn = r.CheckIn; row.CheckOut = r.CheckOut;
            row.LateMinutes = r.LateMinutes; row.WorkedMinutes = r.WorkedMinutes; row.GpsStatus = r.GpsStatus;
            row.Punches = r.Punches.OrderBy(p => p.RecordedAt).Select(ToDto).ToList();
            return row;
        }).OrderBy(r => r.EmployeeCode).ToList();

        var mine = rows.Where(r => r.Assigned && !r.OnOtherProject).ToList();
        return new ProjectAttendanceDayDto
        {
            Date = day,
            GpsAttendanceEnabled = profile?.GpsAttendanceEnabled == true,
            OutOfRangePolicy = profile?.OutOfRangePolicy ?? OutOfRangePolicies.Reject,
            Rows = rows,
            Totals = new ProjectAttendanceTotalsDto
            {
                Assigned = assigned.Count,
                CheckedIn = mine.Count(r => r.Status == AttendanceStatuses.Present),
                CheckedOut = mine.Count(r => r.Status == AttendanceStatuses.Present && r.CheckOut != null),
                OnLeave = rows.Count(r => r.Assigned && r.Status == null && !r.OnOtherProject && r.OnApprovedLeave),
                NotRecorded = rows.Count(r => r.Assigned && r.Status == null && !r.OnOtherProject && !r.OnApprovedLeave),
                PendingExceptions = await _db.Set<AttendanceGpsPunch>()
                    .CountAsync(p => p.ContractId == contractId && p.ReviewStatus == GpsExceptionReviews.Pending, ct),
                ApprovedWorkedMinutes = records.Values
                    .Where(r => r.ContractId == contractId && GpsAttendanceStatuses.IsApproved(r.GpsStatus))
                    .Sum(r => r.WorkedMinutes ?? 0),
            },
        };
    }

    public async Task<List<ProjectAttendanceExceptionDto>> PendingExceptionsAsync(Guid contractId, CancellationToken ct = default)
    {
        await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, ct);
        var punches = await _db.Set<AttendanceGpsPunch>().AsNoTracking().Include(p => p.Record)
            .Where(p => p.ContractId == contractId && p.ReviewStatus == GpsExceptionReviews.Pending)
            .OrderBy(p => p.RecordedAt).ToListAsync(ct);
        var ids = punches.Select(p => p.Record.EmployeeId).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        return punches.Where(p => employees.ContainsKey(p.Record.EmployeeId)).Select(p =>
        {
            var e = employees[p.Record.EmployeeId];
            return new ProjectAttendanceExceptionDto
            {
                PunchId = p.Id, Date = p.Record.Date, EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.NameAr, EmployeeNameEn = e.NameEn,
                Kind = p.Kind, RecordedAt = p.RecordedAt, LocalTime = p.Kind == AttendancePunchKinds.CheckIn ? p.Record.CheckIn : p.Record.CheckOut,
                AccuracyMeters = p.AccuracyMeters, DistanceMeters = p.DistanceMeters, RadiusMeters = p.GeofenceRadiusMeters,
                ExceptionReason = p.ExceptionReason, Latitude = p.Latitude, Longitude = p.Longitude,
            };
        }).ToList();
    }

    public async Task<ProjectAttendanceDayDto> ReviewExceptionAsync(Guid contractId, Guid punchId, bool approve, GpsExceptionReviewDto review, CancellationToken ct = default)
    {
        var contract = await ConstructionProjectService.LoadBoqContractAsync(_db, contractId, ct);
        ConstructionProjectService.EnsureEditable(contract);
        var day = await _attendance.ReviewFieldExceptionAsync(contractId, punchId, approve, review.Note, ct);
        return await DayAsync(contractId, day, ct);
    }

    private static ProjectAttendancePunchDto ToDto(AttendanceGpsPunch p) => new()
    {
        Id = p.Id, Kind = p.Kind, RecordedAt = p.RecordedAt, AccuracyMeters = p.AccuracyMeters, DistanceMeters = p.DistanceMeters,
        RadiusMeters = p.GeofenceRadiusMeters, WithinGeofence = !p.OutsideGeofence, ExceptionReason = p.ExceptionReason,
        ReviewStatus = p.ReviewStatus, ReviewNote = p.ReviewNote, ReviewedBy = p.ReviewedBy, ReviewedAt = p.ReviewedAt,
        // الإحداثيات للاستثناء وحده: داخل النطاق يكفي البعد، فلا يُكشف موقع الموظف بلا حاجة
        Latitude = p.OutsideGeofence ? p.Latitude : null,
        Longitude = p.OutsideGeofence ? p.Longitude : null,
    };
}
