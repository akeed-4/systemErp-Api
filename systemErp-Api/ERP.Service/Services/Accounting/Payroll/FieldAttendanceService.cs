using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الحضور الميداني بالموقع. كل ما يُعتمد عليه يحدده الخادم: الموظف من الجلسة، والمشروع من إسناده النشط (مورد المشروع
/// بنوع عمالة أو مهندس)، والوقت من الخادم بالمنطقة الزمنية للمنشأة، والبعد عن موقع المشروع بمعادلة الخادم. ما يرسله الجهاز
/// (الإحداثيات ودقتها) يُرفض إن ضعفت دقته أو وقع خارج النطاق. التسجيل نفسه في سجل الحضور اليومي عبر <see cref="IAttendanceService"/>
/// فيدخل كشف اليوم والملخص والمسير كما هو.
/// </summary>
public class FieldAttendanceService : IFieldAttendanceService
{
    private static readonly string[] FieldKinds = { ProjectResourceKinds.Labor, ProjectResourceKinds.Engineer };
    private static readonly string[] ExecutionStages = { ContractStageDefinition.Active, ContractStageDefinition.InitialAcceptance };
    private const int MaxReasonLength = 500;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAttendanceService _attendance;
    private readonly TimeProvider _time;

    public FieldAttendanceService(ErpDbContext db, ICurrentUser user, IAttendanceService attendance, TimeProvider time)
    {
        _db = db; _user = user; _attendance = attendance; _time = time;
    }

    public async Task<MyFieldAttendanceDto> TodayAsync(CancellationToken ct = default)
    {
        var me = await EmployeePortalService.CurrentEmployeeAsync(_db, _user, ct);
        return await BuildAsync(me, await NowAsync(ct), ct);
    }

    public Task<MyFieldAttendanceDto> CheckInAsync(GpsPunchRequestDto request, CancellationToken ct = default)
        => PunchAsync(request, AttendancePunchKinds.CheckIn, ct);

    public Task<MyFieldAttendanceDto> CheckOutAsync(GpsPunchRequestDto request, CancellationToken ct = default)
        => PunchAsync(request, AttendancePunchKinds.CheckOut, ct);

    // ---------- داخلي ----------

    private sealed record Now(DateTime Utc, DateTime Local, string TimeZoneId)
    {
        public DateTime Day => Local.Date;
        public int Minutes => (int)Local.TimeOfDay.TotalMinutes;
    }

    /// <summary>مشروع مسند للموظف؛ <see cref="Executing"/> = في التنفيذ (سارٍ أو في الاستلام الابتدائي) فيُسجَّل عليه حضور.</summary>
    private sealed record Assignment(Guid ContractId, string ContractNumber, string ProjectName, string? ProjectNameEn, bool Executing,
        ConstructionProjectProfile? Profile);

    private async Task<MyFieldAttendanceDto> PunchAsync(GpsPunchRequestDto request, string kind, CancellationToken ct)
    {
        var me = await EmployeePortalService.CurrentEmployeeAsync(_db, _user, ct);
        if (!GeoDistance.IsValidCoordinate(request.Latitude, request.Longitude) || request.AccuracyMeters <= 0)
            throw new ValidationFailedException(Messages.FieldAttendanceLocationInvalid);

        var now = await NowAsync(ct);
        // الانصراف على مشروع حضور اليوم نفسه؛ والحضور على المشروع المختار أو الوحيد المسند
        var wanted = kind == AttendancePunchKinds.CheckOut ? await OpenCheckInProjectAsync(me.Id, now.Day, ct) ?? request.ContractId : request.ContractId;
        var project = await ResolveProjectAsync(me.Id, wanted, ct);
        var profile = project.Profile;
        if (profile is not { GpsAttendanceEnabled: true, SiteLatitude: decimal siteLat, SiteLongitude: decimal siteLon })
            throw new ConflictException(Messages.FieldAttendanceGpsDisabled);
        // مشروع من دراسة جدوى: لا تنفيذ إلا بعقد سارٍ وخطة معتمدة (قيد التقرير اليومي نفسه)
        using (_db.SuspendDataScope()) await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, project.ContractId, ct);

        if (request.AccuracyMeters > profile.MaxGpsAccuracyMeters)
            throw new ValidationFailedException(string.Format(Messages.FieldAttendanceAccuracyPoor, Math.Round(request.AccuracyMeters), profile.MaxGpsAccuracyMeters));
        var distance = Math.Round((decimal)GeoDistance.Meters(siteLat, siteLon, request.Latitude, request.Longitude), 1);
        var outside = distance > profile.GeofenceRadiusMeters;
        var reason = Clip(request.ExceptionReason, MaxReasonLength);
        if (outside)
        {
            // خارج النطاق: يُرفض، أو يُقبل استثناءً بسبب يراجعه المشرف إن سمحت سياسة المشروع (الدقة الضعيفة تُرفض دائماً: تُعاد القراءة)
            if (profile.OutOfRangePolicy != OutOfRangePolicies.Exception)
                throw new ValidationFailedException(string.Format(Messages.FieldAttendanceOutsideGeofence, Math.Round(distance), profile.GeofenceRadiusMeters));
            if (reason == null)
                throw new ValidationFailedException(string.Format(Messages.FieldAttendanceExceptionReasonRequired, Math.Round(distance), profile.GeofenceRadiusMeters));
        }

        var punch = new FieldPunchCommand
        {
            EmployeeId = me.Id, ContractId = project.ContractId, Day = now.Day, RecordedAtUtc = now.Utc, LocalMinutes = now.Minutes,
            WorkdayStartMinutes = profile.WorkdayStartMinutes, BreakStartMinutes = profile.BreakStartMinutes, BreakEndMinutes = profile.BreakEndMinutes,
            DeviceCapturedAt = request.CapturedAt?.ToUniversalTime(), Latitude = Math.Round(request.Latitude, 6), Longitude = Math.Round(request.Longitude, 6),
            AccuracyMeters = Math.Round(request.AccuracyMeters, 1), DistanceMeters = distance, RadiusMeters = profile.GeofenceRadiusMeters,
            OutsideGeofence = outside, ExceptionReason = outside ? reason : null,
        };
        if (kind == AttendancePunchKinds.CheckIn) await _attendance.RecordFieldCheckInAsync(punch, ct);
        else await _attendance.RecordFieldCheckOutAsync(punch, ct);
        return await BuildAsync(me, now, ct);
    }

    /// <summary>
    /// المشروع المطلوب إن كان من المسند إليه، أو الوحيد المسند الذي في التنفيذ إن لم يُحدَّد. مشروع لم يُسند إليه مرفوض
    /// (لا يكشف وجوده)، والمسند خارج التنفيذ تعارض.
    /// </summary>
    private async Task<Assignment> ResolveProjectAsync(Guid employeeId, Guid? contractId, CancellationToken ct)
    {
        var assigned = await AssignmentsAsync(employeeId, ct);
        if (contractId is Guid id)
        {
            var chosen = assigned.FirstOrDefault(a => a.ContractId == id) ?? throw new ForbiddenException(Messages.FieldAttendanceProjectNotAssigned);
            return chosen.Executing ? chosen : throw new ConflictException(Messages.FieldAttendanceProjectInactive);
        }
        var executing = assigned.Where(a => a.Executing).ToList();
        return executing.Count switch
        {
            1 => executing[0],
            > 1 => throw new ValidationFailedException(Messages.FieldAttendanceChooseProject),
            _ when assigned.Count > 0 => throw new ConflictException(Messages.FieldAttendanceProjectInactive),
            _ => throw new ForbiddenException(Messages.FieldAttendanceNotAssigned),
        };
    }

    /// <summary>
    /// المشاريع التي أُسند إليها الموظف بمورد نشط (عمالة أو مهندس). الإسناد هو ما يخوّله، لا صلاحية شاشة العقود، فيُعطَّل نطاق
    /// الوصول على العقود هنا (مرشّح المنشأة باقٍ).
    /// </summary>
    private async Task<List<Assignment>> AssignmentsAsync(Guid employeeId, CancellationToken ct)
    {
        var ids = await _db.Set<ProjectResource>().AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.IsActive && FieldKinds.Contains(r.Kind))
            .Select(r => r.ContractId).Distinct().ToListAsync(ct);
        if (ids.Count == 0) return new();

        using var _ = _db.SuspendDataScope();
        var contracts = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.ContractNumber)
            .Select(c => new { c.Id, c.ContractNumber, c.ProjectName, c.ProjectNameEn, c.Title, c.TitleEn, c.Stage })
            .ToListAsync(ct);
        var profiles = await _db.Set<ConstructionProjectProfile>().AsNoTracking().Where(p => ids.Contains(p.ContractId)).ToDictionaryAsync(p => p.ContractId, ct);
        return contracts.Select(c => new Assignment(c.Id, c.ContractNumber, c.ProjectName ?? c.Title, c.ProjectNameEn ?? c.TitleEn,
            ExecutionStages.Contains(c.Stage), profiles.GetValueOrDefault(c.Id))).ToList();
    }

    private Task<Guid?> OpenCheckInProjectAsync(Guid employeeId, DateTime day, CancellationToken ct)
        => _db.Set<AttendanceRecord>().AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.Date == day && r.Source == AttendanceSources.MobileGps && r.CheckOutAt == null)
            .Select(r => r.ContractId).FirstOrDefaultAsync(ct);

    private async Task<Now> NowAsync(CancellationToken ct)
    {
        var zone = await TenantTimeZone.ForCurrentTenantAsync(_db, ct);
        var utc = _time.GetUtcNow().UtcDateTime;
        return new Now(utc, TenantTimeZone.ToLocal(utc, zone), zone.Id);
    }

    private async Task<MyFieldAttendanceDto> BuildAsync(Employee me, Now now, CancellationToken ct)
    {
        var projects = (await AssignmentsAsync(me.Id, ct)).Where(p => p.Executing).ToList();
        var record = await _db.Set<AttendanceRecord>().AsNoTracking().Include(r => r.Punches)
            .FirstOrDefaultAsync(r => r.EmployeeId == me.Id && r.Date == now.Day, ct);
        var anyEnabled = projects.Any(p => p.Profile?.GpsAttendanceEnabled == true);
        return new MyFieldAttendanceDto
        {
            EmployeeCode = me.Code, EmployeeName = me.NameAr, EmployeeNameEn = me.NameEn, Date = now.Day, TimeZoneId = now.TimeZoneId,
            Projects = projects.Select(p => new MyFieldProjectDto
            {
                ContractId = p.ContractId, ContractNumber = p.ContractNumber, ProjectName = p.ProjectName, ProjectNameEn = p.ProjectNameEn,
                GpsAttendanceEnabled = p.Profile?.GpsAttendanceEnabled == true,
                RadiusMeters = p.Profile?.GeofenceRadiusMeters ?? ProjectGeofence.DefaultRadiusMeters,
                MaxAccuracyMeters = p.Profile?.MaxGpsAccuracyMeters ?? ProjectGeofence.DefaultMaxAccuracyMeters,
                AllowsOutOfRangeException = p.Profile?.OutOfRangePolicy == OutOfRangePolicies.Exception,
                WorkdayStart = Clock(p.Profile?.WorkdayStartMinutes ?? 420), WorkdayEnd = Clock(p.Profile?.WorkdayEndMinutes ?? 1020),
            }).ToList(),
            Record = record == null ? null : new MyFieldAttendanceRecordDto
            {
                Status = record.Status, Source = record.Source, ContractId = record.ContractId, CheckIn = record.CheckIn, CheckOut = record.CheckOut,
                CheckInAt = record.CheckInAt, CheckOutAt = record.CheckOutAt, LateMinutes = record.LateMinutes, WorkedMinutes = record.WorkedMinutes,
                GpsStatus = record.GpsStatus,
                Punches = record.Punches.OrderBy(p => p.RecordedAt).Select(p => new MyGpsPunchDto
                {
                    Kind = p.Kind, RecordedAt = p.RecordedAt, AccuracyMeters = p.AccuracyMeters, DistanceMeters = p.DistanceMeters, RadiusMeters = p.GeofenceRadiusMeters,
                    WithinGeofence = !p.OutsideGeofence, ExceptionReason = p.ExceptionReason, ReviewStatus = p.ReviewStatus, ReviewNote = p.ReviewNote,
                }).ToList(),
            },
            CanCheckIn = record == null && anyEnabled,
            CanCheckOut = record is { Source: AttendanceSources.MobileGps, CheckInAt: not null, CheckOutAt: null },
        };
    }

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    private static string? Clip(string? value, int max)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim() is var t && t.Length > max ? t[..max] : t;
}
