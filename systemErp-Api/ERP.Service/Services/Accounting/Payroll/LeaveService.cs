using System.Globalization;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.Extensions.Logging;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// طلبات الإجازات: طلب ← (طلب استكمال ← إعادة إرسال) ← اعتماد/رفض ← إلغاء. الإجازة السنوية لا تتجاوز الرصيد المتاح،
/// والأنواع ذات الحد السنوي لا تتجاوزه، ولا تتداخل إجازتان لموظف؛ والطلب المعاد للاستكمال يبقى حاجزاً لأيامه فلا
/// تُحتسب مرتين عند إعادة إرساله أو تعديله (يُعدَّل الطلب نفسه). إجازة منقوصة الأجر تدخل خصماً في مسير شهرها، فلا
/// يتغيّر وضعها بعد ترحيل ذلك المسير إلا بعكسه. كل انتقال يُسجَّل في سجل التدقيق (ومنه سجل حالات الطلب)،
/// وقرارات شؤون الموظفين تُشعر الموظف صاحب الطلب.
/// </summary>
public class LeaveService : ILeaveService
{
    private const int MaxLeaveDays = 366;

    /// <summary>أحداث سجل الطلب في سجل التدقيق (EntityName = LeaveRequest).</summary>
    public static class Events
    {
        public const string Submit = "submit";
        public const string Update = "update";
        public const string Return = "return";
        public const string Resubmit = "resubmit";
        public const string Approve = "approve";
        public const string Reject = "reject";
        public const string Cancel = "cancel";
        public const string AttachmentAdded = "attachment_add";
        public const string AttachmentDeleted = "attachment_delete";
        public const string AttachmentViewed = "attachment_view";
        /// <summary>تظهر للموظف في سجل طلبه (فتح شؤون الموظفين للمرفق يبقى في سجل التدقيق وحده).</summary>
        public static readonly string[] Visible = { Submit, Update, Return, Resubmit, Approve, Reject, Cancel, AttachmentAdded, AttachmentDeleted };
    }

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly ILogger<LeaveService> _log;

    public LeaveService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit,
        INotificationService notifications, ILogger<LeaveService> log)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _audit = audit; _notifications = notifications; _log = log;
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
            await LogAsync(leave, Events.Submit, leave.Reason, token);
            return Mapper.Map<LeaveRequestDto>(leave);
        }, ct);

    /// <summary>
    /// تعديل طلب لم يُبتّ فيه (معلّق أو معاد للاستكمال). المعاد للاستكمال يعود معلّقاً بإعادة إرساله؛ والطلب نفسه
    /// يُعدَّل فلا تُحتسب أيامه مرتين.
    /// </summary>
    public Task<LeaveRequestDto> UpdateAsync(Guid id, UpdateLeaveRequestDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (!LeaveStatuses.Open.Contains(leave.Status)) throw new ConflictException(Messages.LeaveRequestNotPending);
            var resubmitted = leave.Status == LeaveStatuses.NeedsInfo;
            await FillAsync(leave, request, token);
            await _db.SaveChangesAsync(token);
            await LogAsync(leave, resubmitted ? Events.Resubmit : Events.Update, leave.Reason, token);
            return Mapper.Map<LeaveRequestDto>(leave);
        }, ct);

    /// <summary>طلب استكمال: يعيد الطلب المعلّق لصاحبه بسبب مكتوب (تقرير طبي ناقص مثلاً) ليعدّله ويعيد إرساله.</summary>
    public Task<LeaveRequestDto> ReturnForInfoAsync(Guid id, string? note, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(note)) throw new ValidationFailedException(Messages.LeaveReturnNoteRequired);
            var leave = await FindAsync(id, token);
            if (leave.Status != LeaveStatuses.Pending) throw new ConflictException(Messages.LeaveRequestNotPending);
            return await DecideAsync(leave, LeaveStatuses.NeedsInfo, note, Events.Return, token);
        }, ct);

    public async Task<List<LeaveHistoryEntryDto>> GetHistoryAsync(Guid id, Guid? ownerEmployeeId = null, CancellationToken ct = default)
    {
        if (!await _db.Set<LeaveRequest>().AnyAsync(r => r.Id == id && (ownerEmployeeId == null || r.EmployeeId == ownerEmployeeId), ct))
            throw new NotFoundException(Messages.LeaveRequestNotFound);
        return (await HistoryAsync(_db, new[] { id }, ownerEmployeeId != null, ct))[id].ToList();
    }

    /// <summary>
    /// سجل حالات طلبات الإجازة من سجل التدقيق (لا جدول مكرر). للموظف تُستبعد أحداث الاطلاع الداخلية.
    /// الملاحظة بعد « — » في تفاصيل الحدث.
    /// </summary>
    internal static async Task<ILookup<Guid, LeaveHistoryEntryDto>> HistoryAsync(ErpDbContext db, IReadOnlyCollection<Guid> ids, bool forEmployee, CancellationToken ct)
    {
        var keys = ids.Select(i => i.ToString()).ToList();
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.EntityName == nameof(LeaveRequest) && a.EntityId != null && keys.Contains(a.EntityId)
                && (!forEmployee || Events.Visible.Contains(a.Action)))
            .OrderBy(a => a.Timestamp).Select(a => new { a.EntityId, a.Action, a.PerformedBy, a.Timestamp, a.Details })
            .ToListAsync(ct);
        return rows.ToLookup(a => Guid.Parse(a.EntityId!), a =>
        {
            var at = a.Details.IndexOf(NoteSeparator, StringComparison.Ordinal);
            return new LeaveHistoryEntryDto
            {
                Action = a.Action, PerformedBy = a.PerformedBy, At = a.Timestamp,
                Note = at < 0 ? null : a.Details[(at + NoteSeparator.Length)..],
            };
        });
    }

    private const string NoteSeparator = " — ";

    /// <summary>حدث على الطلب في سجل التدقيق: ملخص الطلب ثم الملاحظة (إن وُجدت) بعد الفاصل.</summary>
    internal Task LogAsync(LeaveRequest leave, string action, string? note, CancellationToken ct)
        => LogEventAsync(_audit, leave, action, note, ct);

    internal static Task LogEventAsync(IAuditService audit, LeaveRequest leave, string action, string? note, CancellationToken ct)
        => audit.LogAsync(action, nameof(LeaveRequest), leave.Id.ToString(),
            $"{leave.RequestNumber} {leave.EmployeeName}: {leave.LeaveTypeName} {leave.StartDate:yyyy-MM-dd} → {leave.EndDate:yyyy-MM-dd} ({leave.Days})"
            + (string.IsNullOrWhiteSpace(note) ? string.Empty : NoteSeparator + note.Trim()), ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var leave = await FindAsync(id, ct);
        if (leave.Status == LeaveStatuses.Approved) throw new ConflictException(Messages.LeaveApprovedCannotBeDeleted);
        // المرفقات تُحذف مع الطلب (بالمعرّف، دون تحميل محتواها)
        foreach (var attachmentId in await _db.Set<LeaveAttachment>().Where(a => a.LeaveRequestId == id).Select(a => a.Id).ToListAsync(ct))
            _db.Remove(new LeaveAttachment { Id = attachmentId, TenantId = leave.TenantId });
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
            // نوع يلزمه مستند (الإجازة المرضية وتقريرها الطبي): لا يُعتمد بلا مرفق
            if (leave.AttachmentCount == 0
                && await _db.Set<LeaveType>().AsNoTracking().AnyAsync(t => t.Id == leave.LeaveTypeId && t.RequiresAttachment, token))
                throw new ValidationFailedException(Messages.LeaveAttachmentRequiredToApprove);
            // الرصيد والحد يُتحقق منهما مجدداً: قد تكون طلبات أخرى اعتُمدت منذ تقديم الطلب
            await EnsureWithinLimitsAsync(leave, employee, token);
            await EnsurePayrollOpenAsync(leave, token);
            return await DecideAsync(leave, LeaveStatuses.Approved, note, Events.Approve, token);
        }, ct);

    public Task<LeaveRequestDto> RejectAsync(Guid id, string? note, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (!LeaveStatuses.Open.Contains(leave.Status)) throw new ConflictException(Messages.LeaveRequestNotPending);
            return await DecideAsync(leave, LeaveStatuses.Rejected, note, Events.Reject, token);
        }, ct);

    public Task<LeaveRequestDto> CancelAsync(Guid id, string? note, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var leave = await FindAsync(id, token);
            if (!LeaveStatuses.Reserving.Contains(leave.Status)) throw new ConflictException(Messages.LeaveRequestAlreadyClosed);
            if (leave.Status == LeaveStatuses.Approved) await EnsurePayrollOpenAsync(leave, token);
            return await DecideAsync(leave, LeaveStatuses.Cancelled, note, Events.Cancel, token);
        }, ct);

    public async Task<LeaveBalanceDto> GetBalanceAsync(Guid employeeId, DateTime? asOf, CancellationToken ct = default)
    {
        var employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new NotFoundException(Messages.LeaveEmployeeNotFound);
        return LeaveBalances.Compute(employee, asOf ?? SaudiTime.Today, await AnnualRequestsAsync(employeeId, null, ct));
    }

    public async Task<List<LeaveBalanceDto>> ListBalancesAsync(CancellationToken ct = default)
    {
        var today = SaudiTime.Today;
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.Code).ToListAsync(ct);
        var requests = (await _db.Set<LeaveRequest>().AsNoTracking()
                .Where(r => r.IsAnnual && LeaveStatuses.Reserving.Contains(r.Status)).ToListAsync(ct))
            .ToLookup(r => r.EmployeeId);
        return employees.Select(e => LeaveBalances.Compute(e, today, requests[e.Id])).ToList();
    }

    // ---------- داخلي ----------

    private async Task<LeaveRequest> FindAsync(Guid id, CancellationToken ct)
        => await _db.Set<LeaveRequest>().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException(Messages.LeaveRequestNotFound);

    private Task<List<LeaveRequest>> AnnualRequestsAsync(Guid employeeId, Guid? exceptId, CancellationToken ct)
        => _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.IsAnnual && r.Id != exceptId
                && LeaveStatuses.Reserving.Contains(r.Status))
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
                && LeaveStatuses.Reserving.Contains(o.Status) && o.StartDate <= end && o.EndDate >= start, ct))
            throw new ConflictException(Messages.LeaveOverlaps);
        await EnsureWithinLimitsAsync(leave, employee, ct, type.MaxDaysPerYear);
    }

    /// <summary>الإجازة السنوية ضمن الرصيد المتاح عند بدايتها، والنوع المحدود ضمن حدّه في سنة بدايتها.</summary>
    private async Task EnsureWithinLimitsAsync(LeaveRequest leave, Employee employee, CancellationToken ct, int? maxDaysPerYear = null)
    {
        if (leave.IsAnnual)
        {
            var asOf = leave.StartDate > SaudiTime.Today ? leave.StartDate : SaudiTime.Today;
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
                && LeaveStatuses.Reserving.Contains(o.Status) && o.StartDate >= yearStart && o.StartDate < yearEnd)
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
        await LogAsync(leave, action, leave.DecisionNote, ct);
        await NotifyEmployeeAsync(leave, action, ct);
        return Mapper.Map<LeaveRequestDto>(leave);
    }

    /// <summary>
    /// يُشعر الموظف صاحب الطلب (إن كان له حساب بوابة) بقرار شؤون الموظفين: اعتماد، رفض، طلب استكمال، أو إلغاء.
    /// إلغاء الموظف طلبه بنفسه لا يُشعره. تعذّر الإشعار لا يُسقط القرار.
    /// </summary>
    private async Task NotifyEmployeeAsync(LeaveRequest leave, string action, CancellationToken ct)
    {
        try
        {
            var userId = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == leave.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
            if (userId == null || userId == _user.UserId) return;
            var title = action switch
            {
                Events.Approve => Messages.LeaveNotifyApprovedTitle,
                Events.Reject => Messages.LeaveNotifyRejectedTitle,
                Events.Return => Messages.LeaveNotifyNeedsInfoTitle,
                _ => Messages.LeaveNotifyCancelledTitle,
            };
            await _notifications.NotifyAsync(new NotifyRequestDto
            {
                RecipientUserId = userId.Value,
                Title = string.Format(title, leave.RequestNumber),
                Body = string.Format(Messages.LeaveNotifyBody, leave.LeaveTypeName, leave.StartDate.ToString("yyyy-MM-dd"), leave.EndDate.ToString("yyyy-MM-dd"))
                    + (leave.DecisionNote == null ? string.Empty : NoteSeparator + leave.DecisionNote),
                RelatedDocType = "leave_request", RelatedDocId = leave.Id, RelatedDocNumber = leave.RequestNumber,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "تعذّر إشعار الموظف بقرار طلب الإجازة {RequestNumber}.", leave.RequestNumber);
        }
    }
}
