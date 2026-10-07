using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class EmployeeService : CrudService<Employee, EmployeeDto, CreateEmployeeDto, UpdateEmployeeDto>, IEmployeeService
{
    public EmployeeService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelEmployee;

    protected override IQueryable<Employee> ApplySearch(IQueryable<Employee> q, string t)
        => q.Where(e => e.Code.Contains(t) || e.NameAr.Contains(t) || (e.NameEn != null && e.NameEn.Contains(t)) || (e.NationalId != null && e.NationalId.Contains(t)));

    protected override async Task ValidateAsync(CreateEmployeeDto d, Employee? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.Code) || string.IsNullOrWhiteSpace(d.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (new[] { d.BasicSalary, d.HousingAllowance, d.TransportAllowance, d.OtherAllowances }.Any(v => v < 0) || d.BasicSalary == 0)
            throw new ValidationFailedException(Messages.EmployeeSalaryInvalid);
        if (d.EmployeeGosiRate is < 0 or > 100 || d.EmployerGosiRate is < 0 or > 100)
            throw new ValidationFailedException(Messages.EmployeeGosiRateInvalid);
        if (await Db.Set<Employee>().AnyAsync(e => e.Code == d.Code && (existing == null || e.Id != existing.Id), ct))
            throw new ConflictException(Messages.EmployeeCodeInUse);
    }

    protected override async Task OnDeletingAsync(Employee e, CancellationToken ct)
    {
        if (await Db.Set<PayrollLine>().AnyAsync(l => l.EmployeeId == e.Id, ct))
            throw new ConflictException(Messages.EmployeeHasPayroll);
    }
}
