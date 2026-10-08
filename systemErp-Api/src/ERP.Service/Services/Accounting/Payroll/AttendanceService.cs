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
/// لأن غيابها وتأخيرها وإضافيها دخلت حسابه.
/// </summary>
public class AttendanceService : IAttendanceService
{
    private const int MaxLateMinutes = 24 * 60;
    private const decimal MaxOvertimeHours = 24;
    private static readonly Regex TimePattern = new(@"^([01]\d|2[0-3]):[0-5]\d$", RegexOptions.Compiled);

    private readonly ErpDbContext _db;
    public AttendanceService(ErpDbContext db) => _db = db;

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
            Upsert(existing.GetValueOrDefault(entry.EmployeeId), entry.EmployeeId, day, entry.Status, entry.LateMinutes, entry.OvertimeHours, entry.CheckIn, entry.CheckOut, entry.Notes);
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
                var record = Upsert(existing.GetValueOrDefault((employeeId, day)), employeeId, day, status, row.LateMinutes, row.OvertimeHours, row.CheckIn, row.CheckOut, row.Notes);
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

    // ---------- داخلي ----------

    /// <summary>يضيف السجل أو يعدّله، أو يحذفه إن كانت الحالة فارغة. يعيد السجل الباقي.</summary>
    private AttendanceRecord? Upsert(AttendanceRecord? record, Guid employeeId, DateTime day, string? status, int lateMinutes, decimal overtimeHours,
        string? checkIn, string? checkOut, string? notes)
    {
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
        record.Status = status;
        // التأخير والإضافي لمن حضر فقط
        var present = status == AttendanceStatuses.Present;
        record.LateMinutes = present ? lateMinutes : 0;
        record.OvertimeHours = present || status == AttendanceStatuses.Off ? overtimeHours : 0;
        record.CheckIn = Blank(checkIn); record.CheckOut = Blank(checkOut); record.Notes = Blank(notes);
        return record;
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
