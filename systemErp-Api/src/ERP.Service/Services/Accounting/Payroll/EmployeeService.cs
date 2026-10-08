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

    protected override async Task OnDeletingAsync(Employee e, CancellationToken ct)
    {
        if (await Db.Set<PayrollLine>().AnyAsync(l => l.EmployeeId == e.Id, ct))
            throw new ConflictException(Messages.EmployeeHasPayroll);
        // مدير قسم يُحذف: يبقى القسم بلا مدير
        foreach (var department in await Db.Set<Department>().Where(x => x.ManagerEmployeeId == e.Id).ToListAsync(ct))
            department.ManagerEmployeeId = null;
    }

    public async Task<List<EmployeeAlertDto>> GetExpiringAsync(int days, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
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
