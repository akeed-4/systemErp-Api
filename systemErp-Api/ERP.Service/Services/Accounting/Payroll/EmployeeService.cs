using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class EmployeeService : CrudService<Employee, EmployeeDto, CreateEmployeeDto, UpdateEmployeeDto>, IEmployeeService
{
    private const int MaxAlertDays = 366;
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex IbanPattern = new(@"^[A-Z]{2}\d{2}[A-Z0-9]{10,30}$", RegexOptions.Compiled);
    private const int SaudiIbanLength = 24;

    public EmployeeService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelEmployee;

    protected override IQueryable<Employee> ApplySearch(IQueryable<Employee> q, string t)
        => q.Where(e => e.Code.Contains(t) || e.NameAr.Contains(t) || (e.NameEn != null && e.NameEn.Contains(t))
            || (e.NationalId != null && e.NationalId.Contains(t)) || (e.Phone != null && e.Phone.Contains(t)));

    protected override IQueryable<Employee> ApplyFilters(IQueryable<Employee> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(e => e.Status == p.Status);

    protected override async Task ValidateAsync(CreateEmployeeDto d, Employee? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (new[] { d.BasicSalary, d.HousingAllowance, d.TransportAllowance, d.OtherAllowances }.Any(v => v < 0) || d.BasicSalary == 0)
            throw new ValidationFailedException(Messages.EmployeeSalaryInvalid);
        if (d.EmployeeGosiRate is < 0 or > 100 || d.EmployerGosiRate is < 0 or > 100)
            throw new ValidationFailedException(Messages.EmployeeGosiRateInvalid);
        if (d.AnnualLeaveDays is < 0 or > 365 || d.OpeningLeaveBalance is < -365 or > 365)
            throw new ValidationFailedException(Messages.EmployeeLeaveSettingsInvalid);
        if (d.OpeningLeaveBalanceDate.HasValue && d.OpeningLeaveBalanceDate.Value.Date < d.HireDate.Date)
            throw new ValidationFailedException(Messages.EmployeeLeaveSettingsInvalid);

        d.Status = string.IsNullOrWhiteSpace(d.Status) ? EmployeeStatuses.Active : d.Status;
        d.IdType = string.IsNullOrWhiteSpace(d.IdType) ? EmployeeIdTypes.NationalId : d.IdType;
        d.ContractType = string.IsNullOrWhiteSpace(d.ContractType) ? EmployeeContractTypes.Unlimited : d.ContractType;
        if (!EmployeeStatuses.All.Contains(d.Status)) throw new ValidationFailedException(Messages.EmployeeStatusInvalid);
        if (!EmployeeIdTypes.All.Contains(d.IdType)) throw new ValidationFailedException(Messages.EmployeeIdTypeInvalid);
        if (!EmployeeContractTypes.All.Contains(d.ContractType)) throw new ValidationFailedException(Messages.EmployeeContractTypeInvalid);

        // العقد محدد المدة له نهاية بعد بدايته (أو بعد تاريخ الالتحاق إن لم تُحدَّد بداية)
        var contractStart = d.ContractStartDate ?? d.HireDate;
        if (d.ContractType == EmployeeContractTypes.Fixed && d.ContractEndDate == null)
            throw new ValidationFailedException(Messages.EmployeeContractEndRequired);
        if (d.ContractEndDate.HasValue && d.ContractEndDate.Value.Date <= contractStart.Date)
            throw new ValidationFailedException(Messages.EmployeeContractDatesInvalid);

        if (d.Status == EmployeeStatuses.Terminated && d.TerminationDate == null)
            throw new ValidationFailedException(Messages.EmployeeTerminationDateRequired);
        if (d.TerminationDate.HasValue && d.TerminationDate.Value.Date < d.HireDate.Date)
            throw new ValidationFailedException(Messages.EmployeeTerminationBeforeHire);

        if (!string.IsNullOrWhiteSpace(d.Email) && !EmailPattern.IsMatch(d.Email.Trim()))
            throw new ValidationFailedException(Messages.EmployeeEmailInvalid);
        if (!string.IsNullOrWhiteSpace(d.Iban))
        {
            d.Iban = d.Iban.Replace(" ", string.Empty).ToUpperInvariant();
            if (!IbanPattern.IsMatch(d.Iban) || (d.Iban.StartsWith("SA") && d.Iban.Length != SaudiIbanLength))
                throw new ValidationFailedException(Messages.EmployeeIbanInvalid);
        }

        if (d.DepartmentId.HasValue && !await Db.Set<Department>().AnyAsync(x => x.Id == d.DepartmentId, ct))
            throw new ValidationFailedException(Messages.DepartmentNotFound);
        if (await Db.Set<Employee>().AnyAsync(e => e.Code == d.Code && (existing == null || e.Id != existing.Id), ct))
            throw new ConflictException(Messages.EmployeeCodeInUse);
    }

    /// <summary>الحذف يمس عدة جداول (العضويات، الطلبات، الحضور، المستخدم المربوط): كلها أو لا شيء.</summary>
    protected override bool Transactional => true;

    /// <summary>
    /// حذف ملف الموظف بعد فحص كل ارتباطاته:
    /// <list type="bullet">
    /// <item>يُمنع بمسيرات الرواتب (أثر مالي) والإجازات والسلف وتسوية نهاية الخدمة، وبالسجلات التشغيلية التي تحمل معرّفه
    /// (موارد المشاريع، تشغيل المعدات وصيانتها، الورشة، أذونات المواد، التأجير والنقل): الرسالة تسمّي ما يمنع.</item>
    /// <item>يُزال معه: عضوياته في المشاريع، وتكلفته المعيارية، وحضوره، ومقدّم الطلب في طلبات الشراء (يبقى اسمه نصاً)، وإدارته للأقسام.</item>
    /// <item>المستخدم المربوط: حساب البوابة يُحذف، ومستخدم النظام يُعطَّل (<see cref="HrLinks.ReleaseLinkedUserAsync"/>).</item>
    /// </list>
    /// </summary>
    protected override async Task OnDeletingAsync(Employee e, CancellationToken ct)
    {
        var runs = (await (from l in Db.Set<PayrollLine>()
                           join r in Db.Set<PayrollRun>() on l.PayrollRunId equals r.Id
                           where l.EmployeeId == e.Id
                           select new { r.RunNumber, r.Period }).Distinct().ToListAsync(ct))
            .OrderBy(r => r.Period).Select(r => r.RunNumber).ToList();
        if (runs.Count > 0)
            throw new ConflictException(string.Format(Messages.EmployeeHasPayroll, runs.Count, string.Join("، ", runs.Take(5))));
        if (await Db.Set<LeaveRequest>().AnyAsync(l => l.EmployeeId == e.Id, ct))
            throw new ConflictException(Messages.EmployeeHasLeaves);
        if (await Db.Set<EmployeeLoan>().AnyAsync(l => l.EmployeeId == e.Id, ct) || await Db.Set<EndOfServiceSettlement>().AnyAsync(s => s.EmployeeId == e.Id, ct))
            throw new ConflictException(Messages.EmployeeHasLoansOrSettlement);
        if (await OperationalLinksAsync(e.Id, ct) is { Count: > 0 } links)
            throw new ConflictException(string.Format(Messages.EmployeeHasOperationalLinks, string.Join("، ", links)));

        // عضويات المشاريع: لا يشير إليها جدول آخر، فتُحذف مع الملف
        Db.RemoveRange(await Db.Set<ProjectMember>().Where(m => m.EmployeeId == e.Id).ToListAsync(ct));
        // مقدّم طلب الشراء: يبقى اسمه وقسمه المنسوخان في الطلب ويزول المعرّف (قيد Restrict)
        foreach (var requisition in await Db.Set<MaterialRequisition>().Where(r => r.RequestedByEmployeeId == e.Id).ToListAsync(ct))
            requisition.RequestedByEmployeeId = null;
        // التكلفة المعيارية إعداد: المستندات ثبّتت تكلفتها على أسطرها
        Db.RemoveRange(await Db.Set<EmployeeStandardCostRate>().Where(r => r.EmployeeId == e.Id).ToListAsync(ct));
        // سجلات الحضور تُحذف مع الموظف: لا أثر مالي لها ما دام بلا مسيرات
        Db.RemoveRange(await Db.Set<AttendanceRecord>().Where(a => a.EmployeeId == e.Id).ToListAsync(ct));
        await HrLinks.ReleaseLinkedUserAsync(Db, e, ct);
        // مدير قسم يُحذف: يبقى القسم بلا مدير
        foreach (var department in await Db.Set<Department>().Where(x => x.ManagerEmployeeId == e.Id).ToListAsync(ct))
            department.ManagerEmployeeId = null;
    }

    /// <summary>السجلات التشغيلية التي تحمل معرّف الموظف بلا قيد في القاعدة: «النوع (العدد)» لكل نوع موجود.</summary>
    private async Task<List<string>> OperationalLinksAsync(Guid id, CancellationToken ct)
    {
        var checks = new (string Label, Func<Task<int>> Count)[]
        {
            (Messages.EmployeeLinkProjectResources, () => Db.Set<ProjectResource>().CountAsync(r => r.EmployeeId == id, ct)),
            (Messages.EmployeeLinkEquipmentOperation, async () => await Db.Set<EquipmentAssignment>().CountAsync(a => a.OperatorEmployeeId == id, ct)
                + await Db.Set<EquipmentMaintenanceOrder>().CountAsync(m => m.TechnicianEmployeeId == id, ct)),
            (Messages.EmployeeLinkWorkshop, () => Db.Set<WorkOrderTimeLog>().CountAsync(l => l.EmployeeId == id, ct)),
            (Messages.EmployeeLinkMaterialIssues, () => Db.Set<ProjectMaterialIssue>().CountAsync(i => i.ReceiverEmployeeId == id, ct)),
            (Messages.EmployeeLinkRental, async () => await Db.Set<RentalContractLine>().CountAsync(l => l.OperatorEmployeeId == id, ct)
                + await Db.Set<RentalTimesheet>().CountAsync(t => t.OperatorEmployeeId == id, ct)
                + await Db.Set<RentalTransportJob>().CountAsync(j => j.DriverEmployeeId == id, ct)),
        };
        var found = new List<string>();
        foreach (var (label, count) in checks)
            if (await count() is var n and > 0) found.Add($"{label} ({n})");
        return found;
    }

    /// <summary>انتقل الموظف إلى «منتهي الخدمة» في هذا التعديل (لا كل حفظ لموظف منتهٍ: من أعاد تنشيط مستخدمه يدوياً لا يُعطَّل ثانية).</summary>
    private bool _terminatedNow;

    protected override Task OnUpdatingAsync(Employee entity, UpdateEmployeeDto dto, CancellationToken ct)
    {
        _terminatedNow = entity.Status == EmployeeStatuses.Terminated
            && Db.Entry(entity).Property(x => x.Status).OriginalValue != EmployeeStatuses.Terminated;
        return Task.CompletedTask;
    }

    protected override Task OnCreatedAsync(Employee entity, CancellationToken ct) => HrLinks.SyncEmployeeAsync(Db, entity.Id, ct);
    protected override Task OnUpdatedAsync(Employee entity, CancellationToken ct) => HrLinks.SyncEmployeeAsync(Db, entity.Id, ct, _terminatedNow);

    public async Task<List<EmployeeAlertDto>> GetExpiringAsync(int days, CancellationToken ct = default)
    {
        var today = SaudiTime.Today;
        var limit = today.AddDays(Math.Clamp(days, 0, MaxAlertDays));
        var employees = await Db.Set<Employee>().AsNoTracking()
            .Where(e => e.Status == EmployeeStatuses.Active
                && ((e.IdExpiryDate != null && e.IdExpiryDate <= limit) || (e.PassportExpiryDate != null && e.PassportExpiryDate <= limit)
                    || (e.ContractEndDate != null && e.ContractEndDate <= limit)
                    || (e.ProbationEndDate != null && e.ProbationEndDate >= today && e.ProbationEndDate <= limit)))
            .Select(e => new { e.Id, e.Code, e.NameAr, e.IdExpiryDate, e.PassportExpiryDate, e.ContractEndDate, e.ProbationEndDate })
            .ToListAsync(ct);

        var alerts = new List<EmployeeAlertDto>();
        foreach (var e in employees)
        {
            void Add(string type, DateTime? date, bool includeExpired = true)
            {
                if (date == null || date.Value.Date > limit || (!includeExpired && date.Value.Date < today)) return;
                alerts.Add(new EmployeeAlertDto
                {
                    EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.NameAr, Type = type,
                    Date = date.Value.Date, DaysLeft = (date.Value.Date - today).Days,
                });
            }
            Add("id", e.IdExpiryDate);
            Add("passport", e.PassportExpiryDate);
            Add("contract", e.ContractEndDate);
            Add("probation", e.ProbationEndDate, includeExpired: false); // فترة تجربة انتهت ليست تنبيهاً
        }
        return alerts.OrderBy(a => a.Date).ThenBy(a => a.EmployeeCode).ToList();
    }

    public async Task<EmployeeSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var rows = await Db.Set<Employee>().AsNoTracking()
            .Select(e => new
            {
                e.Status, e.IdType, e.DepartmentId,
                Gross = e.BasicSalary + e.HousingAllowance + e.TransportAllowance + e.OtherAllowances,
                EmployerGosi = (e.BasicSalary + e.HousingAllowance) * e.EmployerGosiRate / 100m,
            })
            .ToListAsync(ct);
        var departments = await Db.Set<Department>().AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameAr, ct);

        var active = rows.Where(r => r.Status == EmployeeStatuses.Active).ToList();
        var saudi = active.Count(r => r.IdType == EmployeeIdTypes.NationalId);
        return new EmployeeSummaryDto
        {
            ActiveCount = active.Count,
            InactiveCount = rows.Count(r => r.Status == EmployeeStatuses.Inactive),
            TerminatedCount = rows.Count(r => r.Status == EmployeeStatuses.Terminated),
            SaudiCount = saudi,
            NonSaudiCount = active.Count - saudi,
            SaudizationPercent = active.Count == 0 ? 0 : Math.Round(saudi * 100m / active.Count, 1),
            MonthlyGross = active.Sum(r => r.Gross),
            MonthlyEmployerGosi = Math.Round(active.Sum(r => r.EmployerGosi), 2),
            Departments = active.GroupBy(r => r.DepartmentId)
                .Select(g => new DepartmentHeadcountDto
                {
                    DepartmentId = g.Key,
                    DepartmentName = g.Key.HasValue ? departments.GetValueOrDefault(g.Key.Value) : null,
                    Count = g.Count(),
                })
                .OrderByDescending(d => d.Count).ToList(),
        };
    }
}
