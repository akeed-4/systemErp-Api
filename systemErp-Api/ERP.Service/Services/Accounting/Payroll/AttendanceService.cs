using System.Globalization;
using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الحضور اليومي: كشف يوم لكل الموظفين، استيراد جماعي، وملخص شهري. سجلات شهرٍ رُحِّل مسيره مقفلة حتى يُعكس،
/// لأن غيابها وتأخيرها وإضافيها دخلت حسابه. الحضور الميداني (<see cref="AttendanceSources.MobileGps"/>) سجل كغيره، تُحفظ أوقاته
/// وموقعه ومشروعه من الكشف والاستيراد، وتبقى حالته وتأخيره وإضافيه لشؤون الموظفين.
/// </summary>
public class AttendanceService : IAttendanceService
{
    private const int MaxLateMinutes = 24 * 60;
    private const decimal MaxOvertimeHours = 24;
    private const int MaxNoteLength = 500;
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;

    public AttendanceService(ErpDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
    }

    public async Task<List<AttendanceDayRowDto>> GetDayAsync(DateTime date, CancellationToken ct = default)
    {
        var day = date.Date;
        var employees = await _db.Set<Employee>().AsNoTracking()
            .Where(e => e.Status == EmployeeStatuses.Active && e.HireDate <= day).OrderBy(e => e.Code).ToListAsync(ct);
        var records = await _db.Set<AttendanceRecord>().AsNoTracking().Where(r => r.Date == day).ToDictionaryAsync(r => r.EmployeeId, ct);
        var onLeave = (await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(l => l.Status == LeaveStatuses.Approved && l.StartDate <= day && l.EndDate >= day).Select(l => l.EmployeeId).ToListAsync(ct)).ToHashSet();
        return employees.Select(e =>
        {
            var r = records.GetValueOrDefault(e.Id);
            return new AttendanceDayRowDto
            {
                EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.NameAr, Status = r?.Status, LateMinutes = r?.LateMinutes ?? 0,
                OvertimeHours = r?.OvertimeHours ?? 0, CheckIn = r?.CheckIn, CheckOut = r?.CheckOut, Notes = r?.Notes, OnApprovedLeave = onLeave.Contains(e.Id),
                Source = r?.Source, GpsStatus = r?.GpsStatus, WorkedMinutes = r?.WorkedMinutes, ContractId = r?.ContractId,
            };
        }).ToList();
    }

    public async Task<List<AttendanceDayRowDto>> SaveDayAsync(SaveAttendanceDayDto request, CancellationToken ct = default)
    {
        var day = request.Date.Date;
        if (day == default) throw new ValidationFailedException(Messages.AttendanceDateRequired);
        await EnsureMonthOpenAsync(new[] { day }, ct);
        foreach (var entry in request.Entries) Validate(entry.Status, entry.LateMinutes, entry.OvertimeHours, entry.CheckIn, entry.CheckOut);

        var ids = request.Entries.Select(e => e.EmployeeId).Distinct().ToList();
        var known = (await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        if (ids.Any(id => !known.Contains(id))) throw new ValidationFailedException(Messages.LeaveEmployeeNotFound);
        var existing = await _db.Set<AttendanceRecord>().Where(r => r.Date == day && ids.Contains(r.EmployeeId)).ToDictionaryAsync(r => r.EmployeeId, ct);

        foreach (var entry in request.Entries)
            Upsert(existing.GetValueOrDefault(entry.EmployeeId), entry.EmployeeId, day, AttendanceSources.Manual, entry.Status, entry.LateMinutes, entry.OvertimeHours, entry.CheckIn, entry.CheckOut, entry.Notes);
        await _db.SaveChangesAsync(ct);
        return await GetDayAsync(day, ct);
    }

    public async Task<ImportResultDto> ImportAsync(List<AttendanceImportRowDto> rows, CancellationToken ct = default)
    {
        var result = new ImportResultDto { TotalRows = rows.Count };
        var codes = rows.Select(r => r.EmployeeCode?.Trim() ?? string.Empty).Distinct().ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => codes.Contains(e.Code)).ToDictionaryAsync(e => e.Code, e => e.Id, ct);
        var lockedPeriods = await PostedPeriodsAsync(rows.Select(r => r.Date.Date).Distinct(), ct);
        var days = rows.Select(r => r.Date.Date).Distinct().ToList();
        var existing = (await _db.Set<AttendanceRecord>().Where(r => days.Contains(r.Date)).ToListAsync(ct)).ToDictionary(r => (r.EmployeeId, r.Date));

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 2; // الصف 1 في Excel هو رأس الأعمدة
            try
            {
                var day = row.Date.Date;
                if (day == default) throw new ValidationFailedException(Messages.AttendanceDateRequired);
                if (!employees.TryGetValue(row.EmployeeCode?.Trim() ?? string.Empty, out var employeeId))
                    throw new ValidationFailedException(Messages.LeaveEmployeeNotFound);
                if (lockedPeriods.Contains(PeriodOf(day))) throw new ConflictException(string.Format(Messages.AttendanceMonthPosted, PeriodOf(day)));
                var status = string.IsNullOrWhiteSpace(row.Status) ? AttendanceStatuses.Present : row.Status.Trim().ToLowerInvariant();
                Validate(status, row.LateMinutes, row.OvertimeHours, row.CheckIn, row.CheckOut);
                var record = Upsert(existing.GetValueOrDefault((employeeId, day)), employeeId, day, AttendanceSources.Import, status, row.LateMinutes, row.OvertimeHours, row.CheckIn, row.CheckOut, row.Notes);
                if (record != null) existing[(employeeId, day)] = record;
                result.Results.Add(new ImportRowResult { RowNumber = rowNumber, Success = true });
                result.SuccessCount++;
            }
            catch (Exception ex) when (ex is ValidationFailedException or ConflictException)
            {
                result.Results.Add(new ImportRowResult { RowNumber = rowNumber, Success = false, Error = ex.Message });
                result.FailedCount++;
            }
        }
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<List<AttendanceSummaryRowDto>> GetSummaryAsync(string period, CancellationToken ct = default)
    {
        if (!DateTime.TryParseExact(period?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            throw new ValidationFailedException(Messages.PayrollPeriodInvalid);
        var next = month.AddMonths(1);
        var records = (await _db.Set<AttendanceRecord>().AsNoTracking().Where(r => r.Date >= month && r.Date < next).ToListAsync(ct)).ToLookup(r => r.EmployeeId);
        var ids = records.Select(g => g.Key).ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).OrderBy(e => e.Code).ToListAsync(ct);
        return employees.Select(e =>
        {
            var mine = records[e.Id].ToList();
            var absent = mine.Count(r => r.Status == AttendanceStatuses.Absent);
            var late = mine.Sum(r => r.LateMinutes);
            var overtime = mine.Sum(r => r.OvertimeHours);
            return new AttendanceSummaryRowDto
            {
                EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.NameAr,
                PresentDays = mine.Count(r => r.Status == AttendanceStatuses.Present), AbsentDays = absent,
                LeaveDays = mine.Count(r => r.Status == AttendanceStatuses.Leave), OffDays = mine.Count(r => r.Status == AttendanceStatuses.Off),
                LateMinutes = late, OvertimeHours = overtime,
                AttendanceDeduction = AttendancePay.Deduction(e, absent, late), OvertimePay = AttendancePay.Overtime(e, overtime),
            };
        }).ToList();
    }

    // ---------- الحضور الميداني ----------

    public async Task RecordFieldCheckInAsync(FieldPunchCommand punch, CancellationToken ct = default)
    {
        await EnsureMonthOpenAsync(new[] { punch.Day }, ct);
        var record = await _db.Set<AttendanceRecord>().FirstOrDefaultAsync(r => r.EmployeeId == punch.EmployeeId && r.Date == punch.Day, ct);
        if (record != null)
            throw new ConflictException(record.Source == AttendanceSources.MobileGps ? Messages.FieldAttendanceAlreadyCheckedIn : Messages.FieldAttendanceDayRecordedByHr);

        record = new AttendanceRecord
        {
            EmployeeId = punch.EmployeeId, Date = punch.Day, Status = AttendanceStatuses.Present, Source = AttendanceSources.MobileGps,
            ContractId = punch.ContractId, CheckInAt = punch.RecordedAtUtc, CheckIn = Clock(punch.LocalMinutes),
            LateMinutes = Math.Clamp(punch.LocalMinutes - punch.WorkdayStartMinutes, 0, MaxLateMinutes),
        };
        record.Punches.Add(Evidence(punch, AttendancePunchKinds.CheckIn));
        record.GpsStatus = GpsStatusOf(record.Punches);
        _db.Add(record);
        await SaveFieldPunchAsync(Messages.FieldAttendanceAlreadyCheckedIn, ct);
    }

    public async Task RecordFieldCheckOutAsync(FieldPunchCommand punch, CancellationToken ct = default)
    {
        await EnsureMonthOpenAsync(new[] { punch.Day }, ct);
        var record = await _db.Set<AttendanceRecord>().Include(r => r.Punches)
            .FirstOrDefaultAsync(r => r.EmployeeId == punch.EmployeeId && r.Date == punch.Day, ct);
        if (record is not { Source: AttendanceSources.MobileGps, CheckInAt: not null } || record.ContractId != punch.ContractId)
            throw new ConflictException(Messages.FieldAttendanceNoOpenCheckIn);
        if (record.CheckOutAt != null) throw new ConflictException(Messages.FieldAttendanceAlreadyCheckedOut);

        var checkInMinutes = TimeSpan.ParseExact(record.CheckIn!, @"hh\:mm", CultureInfo.InvariantCulture).TotalMinutes;
        var elapsed = Math.Max(0, (int)Math.Floor((punch.RecordedAtUtc - record.CheckInAt.Value).TotalMinutes));
        record.CheckOutAt = punch.RecordedAtUtc;
        record.CheckOut = Clock(punch.LocalMinutes);
        record.WorkedMinutes = Math.Max(0, elapsed - BreakOverlap((int)checkInMinutes, punch.LocalMinutes, punch.BreakStartMinutes, punch.BreakEndMinutes));
        record.Punches.Add(Evidence(punch, AttendancePunchKinds.CheckOut));
        record.GpsStatus = GpsStatusOf(record.Punches);
        await SaveFieldPunchAsync(Messages.FieldAttendanceAlreadyCheckedOut, ct);
    }

    public async Task<DateTime> ReviewFieldExceptionAsync(Guid contractId, Guid punchId, bool approve, string? note, CancellationToken ct = default)
    {
        var punch = await _db.Set<AttendanceGpsPunch>().Include(p => p.Record).ThenInclude(r => r.Punches)
            .FirstOrDefaultAsync(p => p.Id == punchId && p.ContractId == contractId, ct)
            ?? throw new NotFoundException(Messages.AttendanceExceptionNotFound);
        if (punch.ReviewStatus != GpsExceptionReviews.Pending) throw new ConflictException(Messages.AttendanceExceptionNotPending);
        var reviewNote = Blank(note);
        if (reviewNote?.Length > MaxNoteLength) reviewNote = reviewNote[..MaxNoteLength];
        if (!approve && reviewNote == null) throw new ValidationFailedException(Messages.AttendanceExceptionRejectNoteRequired);
        var record = punch.Record;
        if (_user.UserId is Guid reviewer && await _db.Set<Employee>().AnyAsync(e => e.Id == record.EmployeeId && e.UserId == reviewer, ct))
            throw new ForbiddenException(Messages.AttendanceExceptionSelfReview);
        await EnsureMonthOpenAsync(new[] { record.Date }, ct);

        punch.ReviewStatus = approve ? GpsExceptionReviews.Approved : GpsExceptionReviews.Rejected;
        punch.ReviewNote = reviewNote;
        punch.ReviewedByUserId = _user.UserId; punch.ReviewedBy = _user.Name; punch.ReviewedAt = DateTime.UtcNow;
        if (!approve)
        {
            // الحضور غير المعتمد غياب لليوم، والانصراف غير المعتمد لا تُحتسب ساعاته
            if (punch.Kind == AttendancePunchKinds.CheckIn) ApplyHrFields(record, AttendanceStatuses.Absent, 0, 0);
            else record.WorkedMinutes = null;
        }
        record.GpsStatus = GpsStatusOf(record.Punches);
        await _db.SaveChangesAsync(ct);
        return record.Date;
    }

    private static AttendanceGpsPunch Evidence(FieldPunchCommand p, string kind) => new()
    {
        Kind = kind, ContractId = p.ContractId, RecordedAt = p.RecordedAtUtc, DeviceCapturedAt = p.DeviceCapturedAt,
        Latitude = p.Latitude, Longitude = p.Longitude, AccuracyMeters = p.AccuracyMeters, DistanceMeters = p.DistanceMeters, GeofenceRadiusMeters = p.RadiusMeters,
        OutsideGeofence = p.OutsideGeofence,
        ExceptionReason = p.OutsideGeofence ? p.ExceptionReason : null,
        ReviewStatus = p.OutsideGeofence ? GpsExceptionReviews.Pending : null,
    };

    /// <summary>حالة السجل من تسجيلاته: رفض أي استثناء يغلب، ثم الانتظار، ثم الاعتماد، وإلا فهو داخل النطاق.</summary>
    private static string GpsStatusOf(IEnumerable<AttendanceGpsPunch> punches)
    {
        var reviews = punches.Where(p => p.OutsideGeofence).Select(p => p.ReviewStatus).ToList();
        return reviews.Contains(GpsExceptionReviews.Rejected) ? GpsAttendanceStatuses.ExceptionRejected
            : reviews.Contains(GpsExceptionReviews.Pending) ? GpsAttendanceStatuses.ExceptionPending
            : reviews.Count > 0 ? GpsAttendanceStatuses.ExceptionApproved
            : GpsAttendanceStatuses.Valid;
    }

    /// <summary>طلبان متزامنان يتجاوزان الفحص معاً فيردّهما الفهرس الفريد (سجل الموظف في اليوم، ونوع التسجيل في السجل).</summary>
    private async Task SaveFieldPunchAsync(string duplicateMessage, CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ConflictException(duplicateMessage); }
    }

    /// <summary>دقائق الاستراحة الواقعة بين الحضور والانصراف (في اليوم نفسه).</summary>
    private static int BreakOverlap(int from, int to, int? breakStart, int? breakEnd)
        => breakStart is int bs && breakEnd is int be ? Math.Max(0, Math.Min(to, be) - Math.Max(from, bs)) : 0;

    private static string Clock(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

    // ---------- داخلي ----------

    /// <summary>
    /// يضيف السجل أو يعدّله، أو يحذفه إن كانت الحالة فارغة. يعيد السجل الباقي. السجل الميداني لا يُحذف من هنا، ولا تتغير أوقاته
    /// ومشروعه ودليل موقعه: الكشف والاستيراد يعدّلان حالته وتأخيره وإضافيه وملاحظته فقط. غيره يأخذ مصدر آخر من كتبه.
    /// </summary>
    private AttendanceRecord? Upsert(AttendanceRecord? record, Guid employeeId, DateTime day, string source, string? status, int lateMinutes,
        decimal overtimeHours, string? checkIn, string? checkOut, string? notes)
    {
        if (record?.Source == AttendanceSources.MobileGps)
        {
            if (string.IsNullOrWhiteSpace(status)) throw new ConflictException(Messages.AttendanceGpsRecordCannotBeCleared);
            ApplyHrFields(record, status, lateMinutes, overtimeHours);
            record.Notes = Blank(notes);
            return record;
        }
        if (string.IsNullOrWhiteSpace(status))
        {
            if (record != null) _db.Remove(record);
            return null;
        }
        if (record == null)
        {
            record = new AttendanceRecord { EmployeeId = employeeId, Date = day };
            _db.Add(record);
        }
        record.Source = source;
        ApplyHrFields(record, status, lateMinutes, overtimeHours);
        record.CheckIn = Blank(checkIn); record.CheckOut = Blank(checkOut); record.Notes = Blank(notes);
        return record;
    }

    /// <summary>الحالة، والتأخير لمن حضر فقط، والإضافي لمن حضر أو في راحته.</summary>
    private static void ApplyHrFields(AttendanceRecord record, string status, int lateMinutes, decimal overtimeHours)
    {
        record.Status = status;
        var present = status == AttendanceStatuses.Present;
        record.LateMinutes = present ? lateMinutes : 0;
        record.OvertimeHours = present || status == AttendanceStatuses.Off ? overtimeHours : 0;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Validate(string? status, int lateMinutes, decimal overtimeHours, string? checkIn, string? checkOut)
    {
        if (!string.IsNullOrWhiteSpace(status) && !AttendanceStatuses.All.Contains(status)) throw new ValidationFailedException(Messages.AttendanceStatusInvalid);
        if (lateMinutes is < 0 or > MaxLateMinutes || overtimeHours is < 0 or > MaxOvertimeHours) throw new ValidationFailedException(Messages.AttendanceValuesInvalid);
        if ((!string.IsNullOrWhiteSpace(checkIn) && !TimePattern.IsMatch(checkIn.Trim())) || (!string.IsNullOrWhiteSpace(checkOut) && !TimePattern.IsMatch(checkOut.Trim())))
            throw new ValidationFailedException(Messages.AttendanceTimeInvalid);
    }

    private static string PeriodOf(DateTime day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private async Task<HashSet<string>> PostedPeriodsAsync(IEnumerable<DateTime> days, CancellationToken ct)
    {
        var periods = days.Select(PeriodOf).Distinct().ToList();
        return (await _db.Set<PayrollRun>().AsNoTracking().Where(p => p.Status == "posted" && periods.Contains(p.Period)).Select(p => p.Period).ToListAsync(ct)).ToHashSet();
    }

    private async Task EnsureMonthOpenAsync(IEnumerable<DateTime> days, CancellationToken ct)
    {
        var posted = (await PostedPeriodsAsync(days, ct)).OrderBy(p => p).FirstOrDefault();
        if (posted != null) throw new ConflictException(string.Format(Messages.AttendanceMonthPosted, posted));
    }
}
