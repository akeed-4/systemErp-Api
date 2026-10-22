using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// بوابة الموظف. شؤون الموظفين تدعو الموظف فيُنشأ له حساب بدور «موظف» ورابط يعيّن منه كلمة مروره، ثم يقدّم
/// طلبات إجازته بنفسه. الموظف صاحب الطلب يُؤخذ من جلسة الدخول، وطلبه يمرّ بقواعد <see cref="ILeaveService"/> نفسها.
/// </summary>
public class EmployeePortalService : IEmployeePortalService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;
    private readonly ILeaveService _leaves;
    private readonly ILeaveTypeService _leaveTypes;
    private readonly ILeaveAttachmentService _attachments;
    private readonly IPermissionService _permissions;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<EmployeePortalService> _log;

    public EmployeePortalService(ErpDbContext db, ICurrentUser user, ITransactionRunner tx, ILeaveService leaves, ILeaveTypeService leaveTypes,
        ILeaveAttachmentService attachments, IPermissionService permissions, INotificationService notifications, IEmailSender email,
        IConfiguration config, ILogger<EmployeePortalService> log)
    {
        _db = db; _user = user; _tx = tx; _leaves = leaves; _leaveTypes = leaveTypes; _attachments = attachments;
        _permissions = permissions; _notifications = notifications; _email = email; _config = config; _log = log;
    }

    // ---------- شؤون الموظفين: دعوة الموظف ----------

    public async Task<PortalAccessDto> GetAccessAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new NotFoundException(Messages.LeaveEmployeeNotFound);
        var user = employee.UserId == null ? null : await _db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == employee.UserId, ct);
        return Access(user);
    }

    public async Task<PortalAccessDto> InviteAsync(Guid employeeId, CancellationToken ct = default)
    {
        var (access, user, token) = await _tx.RunAsync(async tk =>
        {
            var employee = await _db.Set<Employee>().FirstOrDefaultAsync(e => e.Id == employeeId, tk)
                ?? throw new NotFoundException(Messages.LeaveEmployeeNotFound);
            if (employee.Status != EmployeeStatuses.Active) throw new ValidationFailedException(Messages.PortalEmployeeNotActive);
            var email = employee.Email?.Trim().ToLower();
            if (string.IsNullOrEmpty(email)) throw new ValidationFailedException(Messages.PortalEmailRequired);

            var linked = employee.UserId == null ? null : await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == employee.UserId, tk);
            // البريد فريد عبر كل المنشآت (به يُعرف المستخدم عند الدخول)
            // صاحب البريد يُعرف من الدليل (عبر المنشآت)، ويُقرأ للتعديل من قاعدة هذه المنشأة
            var holder = linked?.Email == email ? null
                : await _db.Catalog.Set<User>().IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.Email.ToLower() == email).Select(u => new { u.Id, u.TenantId }).FirstOrDefaultAsync(tk);
            if (holder != null && holder.TenantId != _db.CurrentTenantId) throw new ConflictException(Messages.EmailAlreadyRegistered);
            var owner = linked?.Email == email ? linked
                : holder == null ? null : await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == holder.Id, tk);
            if (owner != null && owner.Id != linked?.Id)
            {
                if (await _db.Set<Employee>().AnyAsync(e => e.UserId == owner.Id && e.Id != employee.Id, tk))
                    throw new ConflictException(Messages.PortalUserLinkedToAnotherEmployee);
                // حساب بوابة لم يُفعَّل بعد وتغيّر بريد الموظف: يُترك معطّلاً ويُربط الموظف بصاحب البريد الجديد
                if (linked is { Role: UserRole.Employee }) linked.IsActive = false;
                linked = owner; // مستخدم للنظام هو نفسه موظف: يُربط بحسابه القائم دون تغيير دوره
            }
            else if (linked == null)
            {
                linked = new User
                {
                    Id = Guid.NewGuid(), Name = employee.NameAr, Email = email, Phone = employee.Phone ?? string.Empty, Role = UserRole.Employee,
                    JobTitle = employee.JobTitle, AvatarInitials = AuthService.Initials(employee.NameAr),
                };
                _db.Add(linked);
            }
            else if (linked.PasswordHash == null) linked.Email = email;

            employee.UserId = linked.Id;
            if (linked.Role == UserRole.Employee) linked.IsActive = true;

            string? issued = null;
            DateTime? expires = null;
            if (linked.PasswordHash == null)
            {
                await ExpireInvitesAsync(linked.Id, tk);
                (issued, var hash) = InviteTokens.New();
                expires = DateTime.UtcNow.AddDays(InviteTokens.ValidDays);
                _db.Add(new UserInvite { UserId = linked.Id, TokenHash = hash, ExpiresAt = expires.Value });
            }
            await _db.SaveChangesAsync(tk);

            var result = Access(linked);
            result.InviteToken = issued;
            result.InviteExpiresAt = expires;
            return (result, linked, issued);
        }, ct);

        if (token != null) access.EmailSent = await SendInviteAsync(user, token, ct);
        return access;
    }

    public async Task<PortalAccessDto> RevokeAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _db.Set<Employee>().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new NotFoundException(Messages.LeaveEmployeeNotFound);
        var user = employee.UserId == null ? null : await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == employee.UserId, ct);
        if (user == null) return Access(null);

        await ExpireInvitesAsync(user.Id, ct);
        // حساب البوابة يُعطَّل؛ مستخدم النظام (محاسب مثلاً) يُفكّ ربطه بالموظف فقط ويبقى حسابه
        if (user.Role == UserRole.Employee) user.IsActive = false;
        else { employee.UserId = null; user = null; }
        await _db.SaveChangesAsync(ct);
        return Access(user);
    }

    // ---------- الموظف: طلباته ----------

    public async Task<MyLeaveOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        // القراءة عبر الخدمة تنشئ الأنواع المعتادة لمنشأة لم تفتح شاشة الإجازات بعد
        var types = (await _leaveTypes.ListAsync(new PaginationParams { PageSize = PaginationParams.MaxPageSize, SortBy = nameof(LeaveType.Code), IsDescending = false }, ct))
            .Items.Where(t => t.IsActive).ToList();

        var requests = (await _db.Set<LeaveRequest>().AsNoTracking().Where(r => r.EmployeeId == me.Id)
                .OrderByDescending(r => r.StartDate).ThenByDescending(r => r.RequestNumber).ToListAsync(ct))
            .Select(Mapper.Map<MyLeaveRequestDto>).ToList();
        var ids = requests.Where(r => r.AttachmentCount > 0).Select(r => r.Id).ToList();
        if (ids.Count > 0)
        {
            var attachments = (await LeaveAttachmentService.Metadata(_db.Set<LeaveAttachment>().AsNoTracking().Where(a => ids.Contains(a.LeaveRequestId)))
                .OrderBy(a => a.CreatedAt).ToListAsync(ct)).ToLookup(a => a.LeaveRequestId);
            foreach (var r in requests) r.Attachments = attachments[r.Id].ToList();
        }
        if (requests.Count > 0)
        {
            var history = await LeaveService.HistoryAsync(_db, requests.Select(r => r.Id).ToList(), forEmployee: true, ct);
            foreach (var r in requests) r.History = history[r.Id].ToList();
        }

        return new MyLeaveOverviewDto
        {
            EmployeeId = me.Id, EmployeeCode = me.Code, EmployeeName = me.NameAr, JobTitle = me.JobTitle,
            Balance = await _leaves.GetBalanceAsync(me.Id, null, ct), LeaveTypes = types, Requests = requests,
            TypeUsage = await TypeUsageAsync(me.Id, types, ct),
        };
    }

    /// <summary>استهلاك الأنواع المحدودة سنوياً في السنة الحالية (بنفس قاعدة الحد في LeaveService: سنة بداية الإجازة).</summary>
    private async Task<List<LeaveTypeUsageDto>> TypeUsageAsync(Guid employeeId, List<LeaveTypeDto> types, CancellationToken ct)
    {
        var limited = types.Where(t => !t.IsAnnual && t.MaxDaysPerYear != null).ToList();
        if (limited.Count == 0) return new();
        var year = SaudiTime.Today.Year;
        var (from, to) = (new DateTime(year, 1, 1), new DateTime(year + 1, 1, 1));
        var typeIds = limited.Select(t => t.Id).ToList();
        var rows = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && typeIds.Contains(r.LeaveTypeId) && LeaveStatuses.Reserving.Contains(r.Status)
                && r.StartDate >= from && r.StartDate < to)
            .Select(r => new { r.LeaveTypeId, r.Status, r.Days }).ToListAsync(ct);
        return limited.Select(t =>
        {
            var used = rows.Where(r => r.LeaveTypeId == t.Id && r.Status == LeaveStatuses.Approved).Sum(r => r.Days);
            var pending = rows.Where(r => r.LeaveTypeId == t.Id && r.Status != LeaveStatuses.Approved).Sum(r => r.Days);
            return new LeaveTypeUsageDto
            {
                LeaveTypeId = t.Id, LeaveTypeName = t.NameAr, Year = year, MaxDaysPerYear = t.MaxDaysPerYear!.Value,
                Used = used, Pending = pending, Remaining = Math.Max(0, t.MaxDaysPerYear!.Value - used - pending),
            };
        }).ToList();
    }

    public async Task<LeaveRequestDto> UpdateLeaveAsync(Guid id, UpdateMyLeaveRequestDto request, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var status = await _db.Set<LeaveRequest>().AsNoTracking().Where(r => r.Id == id && r.EmployeeId == me.Id).Select(r => r.Status).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.LeaveRequestNotFound);
        var updated = await _leaves.UpdateAsync(id, new UpdateLeaveRequestDto
        {
            EmployeeId = me.Id, LeaveTypeId = request.LeaveTypeId, StartDate = request.StartDate, EndDate = request.EndDate, Reason = request.Reason,
        }, ct);
        // إعادة إرسال بعد الاستكمال: يعود للمعتمدين
        if (status == LeaveStatuses.NeedsInfo) await NotifyApproversAsync(updated, ct, resubmitted: true);
        return updated;
    }

    /// <summary>
    /// رواتب الموظف: راتبه الحالي من ملفه (الآيبان مخفي إلا آخر أربعة)، وسطره في كل مسير معتمد أو معكوس.
    /// المسودة لا تظهر قبل اعتمادها، ولا يرى الموظف إلا سطره.
    /// </summary>
    public async Task<MyPayrollDto> GetPayrollAsync(CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var department = me.DepartmentId == null ? null
            : await _db.Set<Department>().AsNoTracking().Where(d => d.Id == me.DepartmentId).Select(d => d.NameAr).FirstOrDefaultAsync(ct);
        var slips = await (from line in _db.Set<PayrollLine>().AsNoTracking()
                           join run in _db.Set<PayrollRun>().AsNoTracking() on line.PayrollRunId equals run.Id
                           where line.EmployeeId == me.Id && (run.Status == "posted" || run.Status == "reversed")
                           orderby run.Period descending, run.Date descending
                           select new { line, run.Id, run.RunNumber, run.Period, run.Date, run.Status }).ToListAsync(ct);
        var iban = me.Iban?.Replace(" ", string.Empty);
        return new MyPayrollDto
        {
            EmployeeCode = me.Code, EmployeeName = me.NameAr, JobTitle = me.JobTitle, DepartmentName = department,
            BasicSalary = me.BasicSalary, HousingAllowance = me.HousingAllowance, TransportAllowance = me.TransportAllowance, OtherAllowances = me.OtherAllowances,
            TotalSalary = me.BasicSalary + me.HousingAllowance + me.TransportAllowance + me.OtherAllowances,
            EmployeeGosiRate = me.EmployeeGosiRate, BankName = me.BankName,
            MaskedIban = string.IsNullOrEmpty(iban) ? null : iban.Length <= 4 ? iban : new string('•', iban.Length - 4) + iban[^4..],
            Payslips = slips.Select(s =>
            {
                var slip = Mapper.Map<MyPayslipDto>(s.line);
                slip.RunId = s.Id; slip.RunNumber = s.RunNumber; slip.Period = s.Period; slip.Date = s.Date; slip.Status = s.Status;
                slip.EmployeeGosiRate = me.EmployeeGosiRate;
                return slip;
            }).ToList(),
        };
    }

    public async Task<LeaveRequestDto> CreateLeaveAsync(CreateMyLeaveRequestDto request, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var created = await _leaves.CreateAsync(new CreateLeaveRequestDto
        {
            EmployeeId = me.Id, LeaveTypeId = request.LeaveTypeId, StartDate = request.StartDate, EndDate = request.EndDate, Reason = request.Reason,
        }, ct);
        await NotifyApproversAsync(created, ct);
        return created;
    }

    public async Task<LeaveRequestDto> CancelLeaveAsync(Guid id, CancellationToken ct = default)
    {
        var me = await MeAsync(ct);
        var leave = await _db.Set<LeaveRequest>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == me.Id, ct)
            ?? throw new NotFoundException(Messages.LeaveRequestNotFound);
        // إجازة معتمدة يلغيها من اعتمدها (شؤون الموظفين)، لا الموظف؛ والقائمة (معلّقة أو بانتظار الاستكمال) يلغيها صاحبها
        if (!LeaveStatuses.Open.Contains(leave.Status)) throw new ConflictException(Messages.LeaveRequestNotPending);
        return await _leaves.CancelAsync(id, null, ct);
    }

    public async Task<LeaveAttachmentDto> AddAttachmentAsync(Guid leaveRequestId, string fileName, byte[] content, CancellationToken ct = default)
        => await _attachments.AddAsync(leaveRequestId, fileName, content, (await MeAsync(ct)).Id, ct);

    public async Task<LeaveAttachmentFileDto> GetAttachmentAsync(Guid leaveRequestId, Guid attachmentId, CancellationToken ct = default)
        => await _attachments.GetFileAsync(leaveRequestId, attachmentId, (await MeAsync(ct)).Id, ct);

    public async Task DeleteAttachmentAsync(Guid leaveRequestId, Guid attachmentId, CancellationToken ct = default)
        => await _attachments.DeleteAsync(leaveRequestId, attachmentId, (await MeAsync(ct)).Id, ct);

    // ---------- داخلي ----------

    /// <summary>ملف الموظف المرتبط بالمستخدم الحالي؛ من لا ملف له أو ليس على رأس العمل لا يدخل البوابة.</summary>
    private async Task<Employee> MeAsync(CancellationToken ct)
    {
        var userId = _user.UserId ?? throw new UnauthorizedAppException();
        var me = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId, ct)
            ?? throw new ForbiddenException(Messages.PortalNoEmployeeProfile);
        if (me.Status != EmployeeStatuses.Active) throw new ForbiddenException(Messages.PortalEmployeeNotActive);
        return me;
    }

    private static PortalAccessDto Access(User? user) => new()
    {
        Status = user == null ? PortalAccessStatuses.None
            : !user.IsActive ? PortalAccessStatuses.Disabled
            : user.PasswordHash == null ? PortalAccessStatuses.Invited
            : PortalAccessStatuses.Active,
        Email = user?.Email,
        LastLoginAt = user?.LastLoginAt,
    };

    private async Task ExpireInvitesAsync(Guid userId, CancellationToken ct)
    {
        foreach (var old in await _db.Set<UserInvite>().Where(i => i.UserId == userId && i.UsedAt == null).ToListAsync(ct))
            old.UsedAt = DateTime.UtcNow;
    }

    /// <summary>يرسل الرابط لبريد الموظف إن ضُبط البريد وعنوان الواجهة؛ وإلا تنسخه شؤون الموظفين وترسله بنفسها.</summary>
    private async Task<bool> SendInviteAsync(User user, string token, CancellationToken ct)
    {
        var frontend = _config["App:FrontendUrl"];
        if (string.IsNullOrWhiteSpace(frontend)) frontend = _config["Paymob:FrontendUrl"];
        if (!_email.IsConfigured || string.IsNullOrWhiteSpace(frontend)) return false;
        try
        {
            var company = await _db.Set<Tenant>().AsNoTracking().Where(t => t.Id == user.TenantId).Select(t => t.NameAr).FirstOrDefaultAsync(ct);
            await _email.SendAsync(user.Email, InviteEmail.Subject,
                InviteEmail.Body(user.Name, company ?? string.Empty, $"{frontend.TrimEnd('/')}/activate/{token}", InviteTokens.ValidDays), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "تعذّر إرسال دعوة بوابة الموظف للمستخدم {UserId}.", user.Id);
            return false;
        }
    }

    /// <summary>إشعار لكل من يملك اعتماد شؤون الموظفين بطلب جديد من البوابة. تعذّر الإشعار لا يُسقط الطلب.</summary>
    private async Task NotifyApproversAsync(LeaveRequestDto leave, CancellationToken ct, bool resubmitted = false)
    {
        try
        {
            var users = await _db.Set<User>().AsNoTracking().Where(u => u.IsActive && u.Role != UserRole.Employee).ToListAsync(ct);
            foreach (var u in users)
            {
                if (u.Role is not (UserRole.Owner or UserRole.Admin))
                {
                    var hr = (await _permissions.GetForUserOrRoleAsync(u.Id, RoleIds.From(u.Role), ct)).FirstOrDefault(p => p.ScreenId == "hr");
                    if (hr is { CanApprove: false } or { CanView: false }) continue;
                }
                await _notifications.NotifyAsync(new NotifyRequestDto
                {
                    RecipientUserId = u.Id,
                    Title = string.Format(resubmitted ? Messages.PortalLeaveResubmittedTitle : Messages.PortalLeaveNotificationTitle, leave.EmployeeName),
                    Body = string.Format(Messages.PortalLeaveNotificationBody, leave.LeaveTypeName, leave.StartDate.ToString("yyyy-MM-dd"), leave.EndDate.ToString("yyyy-MM-dd"), leave.Days),
                    RelatedDocType = "leave_request", RelatedDocId = leave.Id, RelatedDocNumber = leave.RequestNumber,
                }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "تعذّر إشعار المعتمدين بطلب الإجازة {RequestNumber}.", leave.RequestNumber);
        }
    }
}
