using System.Globalization;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// طلبات الإجازات: طلب ← اعتماد/رفض ← إلغاء. الإجازة السنوية لا تتجاوز الرصيد المتاح، والأنواع ذات الحد السنوي
/// لا تتجاوزه، ولا تتداخل إجازتان لموظف. إجازة منقوصة الأجر تدخل خصماً في مسير شهرها، فلا يتغيّر وضعها بعد
/// ترحيل ذلك المسير إلا بعكسه.
/// </summary>
public class LeaveService : ILeaveService
{
    private const int MaxLeaveDays = 366;

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public LeaveService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    public Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => EntityLoader.LoadAsync(_db.Set<LeaveRequest>().AsNoTracking(), options, Mapper.Map<LeaveRequestDto>, ct,
            EntityLoader.Desc(nameof(LeaveRequest.StartDate)), EntityLoader.Desc(nameof(LeaveRequest.RequestNumber)));

    public async Task<LeaveRequestDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<LeaveRequestDto>(await _db.Set<LeaveRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(Messages.LeaveRequestNotFound));

    public Task<LeaveRequestDto> CreateAsync(CreateLeaveRequestDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = new LeaveRequest { Id = Guid.NewGuid() };
            await FillAsync(leave, request, token);
            leave.RequestNumber = await _numbers.NextAsync("leave_request", "LV-", token);
            _db.Add(leave);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<LeaveRequestDto>(leave);
        }, ct);

    public Task<LeaveRequestDto> UpdateAsync(Guid id, UpdateLeaveRequestDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (leave.Status != LeaveStatuses.Pending) throw new ConflictException(Messages.LeaveRequestNotPending);
            await FillAsync(leave, request, token);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<LeaveRequestDto>(leave);
        }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var leave = await FindAsync(id, ct);
        if (leave.Status == LeaveStatuses.Approved) throw new ConflictException(Messages.LeaveApprovedCannotBeDeleted);
        _db.Remove(leave);
        await _db.SaveChangesAsync(ct);
    }

    public Task<LeaveRequestDto> ApproveAsync(Guid id, string? note, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (leave.Status != LeaveStatuses.Pending) throw new ConflictException(Messages.LeaveRequestNotPending);
            var employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == leave.EmployeeId, token)
                ?? throw new NotFoundException(Messages.LeaveEmployeeNotFound);
            // الرصيد والحد يُتحقق منهما مجدداً: قد تكون طلبات أخرى اعتُمدت منذ تقديم الطلب
            await EnsureWithinLimitsAsync(leave, employee, token);
            await EnsurePayrollOpenAsync(leave, token);
            return await DecideAsync(leave, LeaveStatuses.Approved, note, "approve", token);
        }, ct);

    public Task<LeaveRequestDto> RejectAsync(Guid id, string? note, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (leave.Status != LeaveStatuses.Pending) throw new ConflictException(Messages.LeaveRequestNotPending);
            return await DecideAsync(leave, LeaveStatuses.Rejected, note, "reject", token);
        }, ct);

    public Task<LeaveRequestDto> CancelAsync(Guid id, string? note, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (leave.Status is not (LeaveStatuses.Pending or LeaveStatuses.Approved)) throw new ConflictException(Messages.LeaveRequestAlreadyClosed);
            if (leave.Status == LeaveStatuses.Approved) await EnsurePayrollOpenAsync(leave, token);
            return await DecideAsync(leave, LeaveStatuses.Cancelled, note, "cancel", token);
        }, ct);

    public async Task<LeaveBalanceDto> GetBalanceAsync(Guid employeeId, DateTime? asOf, CancellationToken ct = default)
    {
        var employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new NotFoundException(Messages.LeaveEmployeeNotFound);
        return LeaveBalances.Compute(employee, asOf ?? DateTime.UtcNow.Date, await AnnualRequestsAsync(employeeId, null, ct));
    }

    public async Task<List<LeaveBalanceDto>> ListBalancesAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.Code).ToListAsync(ct);
        var requests = (await _db.Set<LeaveRequest>().AsNoTracking()
                .Where(r => r.IsAnnual && (r.Status == LeaveStatuses.Approved || r.Status == LeaveStatuses.Pending)).ToListAsync(ct))
            .ToLookup(r => r.EmployeeId);
        return employees.Select(e => LeaveBalances.Compute(e, today, requests[e.Id])).ToList();
    }

    // ---------- داخلي ----------

    private async Task<LeaveRequest> FindAsync(Guid id, CancellationToken ct)
        => await _db.Set<LeaveRequest>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException(Messages.LeaveRequestNotFound);

    private Task<List<LeaveRequest>> AnnualRequestsAsync(Guid employeeId, Guid? exceptId, CancellationToken ct)
        => _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.IsAnnual && r.Id != exceptId
                && (r.Status == LeaveStatuses.Approved || r.Status == LeaveStatuses.Pending))
            .ToListAsync(ct);

    /// <summary>يتحقق من الطلب وينسخ بيانات الموظف ونوع الإجازة إليه.</summary>
    private async Task FillAsync(LeaveRequest leave, CreateLeaveRequestDto r, CancellationToken ct)
    {
        var start = r.StartDate.Date;
        var end = r.EndDate.Date;
        if (start == default || end == default || end < start) throw new ValidationFailedException(Messages.LeaveDatesInvalid);
        var days = LeaveBalances.Span(start, end);
        if (days > MaxLeaveDays) throw new ValidationFailedException(Messages.LeaveDatesInvalid);

        var employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == r.EmployeeId, ct)
            ?? throw new ValidationFailedException(Messages.LeaveEmployeeNotFound);
        if (employee.Status != EmployeeStatuses.Active) throw new ValidationFailedException(Messages.LeaveEmployeeNotActive);
        if (start < employee.HireDate.Date) throw new ValidationFailedException(Messages.LeaveBeforeHireDate);
        var type = await _db.Set<LeaveType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.LeaveTypeId, ct)
            ?? throw new ValidationFailedException(Messages.LeaveTypeNotFound);
        if (!type.IsActive) throw new ValidationFailedException(Messages.LeaveTypeInactive);

        leave.EmployeeId = employee.Id; leave.EmployeeCode = employee.Code; leave.EmployeeName = employee.NameAr;
        leave.LeaveTypeId = type.Id; leave.LeaveTypeName = type.NameAr; leave.IsAnnual = type.IsAnnual; leave.PayPercent = type.PayPercent;
        leave.StartDate = start; leave.EndDate = end; leave.Days = days;
        leave.Reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim();
        leave.Status = LeaveStatuses.Pending;

        if (await _db.Set<LeaveRequest>().AnyAsync(o => o.EmployeeId == employee.Id && o.Id != leave.Id
                && (o.Status == LeaveStatuses.Pending || o.Status == LeaveStatuses.Approved) && o.StartDate <= end && o.EndDate >= start, ct))
            throw new ConflictException(Messages.LeaveOverlaps);
        await EnsureWithinLimitsAsync(leave, employee, ct, type.MaxDaysPerYear);
    }

    /// <summary>الإجازة السنوية ضمن الرصيد المتاح عند بدايتها، والنوع المحدود ضمن حدّه في سنة بدايتها.</summary>
    private async Task EnsureWithinLimitsAsync(LeaveRequest leave, Employee employee, CancellationToken ct, int? maxDaysPerYear = null)
    {
        if (leave.IsAnnual)
        {
            var asOf = leave.StartDate > DateTime.UtcNow.Date ? leave.StartDate : DateTime.UtcNow.Date;
            var balance = LeaveBalances.Compute(employee, asOf, await AnnualRequestsAsync(employee.Id, leave.Id, ct));
            if (leave.Days > balance.Available)
                throw new ValidationFailedException(string.Format(Messages.LeaveBalanceInsufficient, balance.Available.ToString("0.##", CultureInfo.InvariantCulture)));
            return;
        }

        maxDaysPerYear ??= await _db.Set<LeaveType>().AsNoTracking().Where(t => t.Id == leave.LeaveTypeId).Select(t => t.MaxDaysPerYear).FirstOrDefaultAsync(ct);
        if (maxDaysPerYear == null) return;
        var yearStart = new DateTime(leave.StartDate.Year, 1, 1);
        var yearEnd = yearStart.AddYears(1);
        var used = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(o => o.EmployeeId == employee.Id && o.LeaveTypeId == leave.LeaveTypeId && o.Id != leave.Id
                && (o.Status == LeaveStatuses.Pending || o.Status == LeaveStatuses.Approved) && o.StartDate >= yearStart && o.StartDate < yearEnd)
            .SumAsync(o => (int?)o.Days, ct) ?? 0;
        if (used + leave.Days > maxDaysPerYear)
            throw new ValidationFailedException(string.Format(Messages.LeaveTypeLimitExceeded, maxDaysPerYear, Math.Max(0, maxDaysPerYear.Value - used)));
    }

    /// <summary>إجازة منقوصة الأجر لا يتغيّر وضعها إن كان مسير أحد أشهرها مرحّلاً (خصمها محسوب فيه أو فاته).</summary>
    private async Task EnsurePayrollOpenAsync(LeaveRequest leave, CancellationToken ct)
    {
        if (leave.PayPercent >= 100) return;
        var periods = new List<string>();
        for (var month = new DateTime(leave.StartDate.Year, leave.StartDate.Month, 1); month <= leave.EndDate; month = month.AddMonths(1))
            periods.Add(month.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        var posted = await _db.Set<PayrollRun>().AsNoTracking()
            .Where(p => p.Status == "posted" && periods.Contains(p.Period)).Select(p => p.Period).OrderBy(p => p).FirstOrDefaultAsync(ct);
        if (posted != null) throw new ConflictException(string.Format(Messages.LeaveAffectsPostedPayroll, posted));
    }

    private async Task<LeaveRequestDto> DecideAsync(LeaveRequest leave, string status, string? note, string action, CancellationToken ct)
    {
        leave.Status = status;
        leave.DecidedBy = _user.Name ?? _user.Email;
        leave.DecidedAt = DateTime.UtcNow;
        leave.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(action, nameof(LeaveRequest), leave.Id.ToString(),
            $"{leave.RequestNumber} {leave.EmployeeName}: {leave.LeaveTypeName} {leave.StartDate:yyyy-MM-dd} → {leave.EndDate:yyyy-MM-dd} ({leave.Days})", ct);
        return Mapper.Map<LeaveRequestDto>(leave);
    }
}
