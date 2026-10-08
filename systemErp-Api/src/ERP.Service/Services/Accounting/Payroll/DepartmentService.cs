using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class DepartmentService : CrudService<Department, DepartmentDto, CreateDepartmentDto, UpdateDepartmentDto>, IDepartmentService
{
    public DepartmentService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelDepartment;

    protected override IQueryable<Department> ApplySearch(IQueryable<Department> q, string t)
        => q.Where(d => d.Code.Contains(t) || d.NameAr.Contains(t) || (d.NameEn != null && d.NameEn.Contains(t)));

    protected override async Task ValidateAsync(CreateDepartmentDto dto, Department? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.NameAr))
            throw new ValidationFailedException(Messages.CodeAndArabicNameRequired);
        if (dto.ManagerEmployeeId.HasValue && !await Db.Set<Employee>().AnyAsync(e => e.Id == dto.ManagerEmployeeId, ct))
            throw new ValidationFailedException(Messages.DepartmentManagerNotFound);
        if (await Db.Set<Department>().AnyAsync(d => d.Code == dto.Code && (existing == null || d.Id != existing.Id), ct))
            throw new ConflictException(Messages.DepartmentCodeInUse);
    }

    protected override async Task OnDeletingAsync(Department entity, CancellationToken ct)
    {
        if (await Db.Set<Employee>().AnyAsync(e => e.DepartmentId == entity.Id, ct))
            throw new ConflictException(Messages.DepartmentHasEmployees);
    }
}
