using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// سجل التكلفة المعيارية لساعة الموظف بغرضه. الفني يتطلب إضافة الورش وشاشة إعداداتها، والمشغّل إضافة التأجير وشاشة إعداداته (العرض
/// للقراءة، والتعديل للإضافة والحذف). سطر واحد للموظف والغرض في اليوم (فهرس فريد)، والموظف الجديد عليه نشط. الحذف لا يمس ما ثبّتته
/// المستندات من تكلفة على أسطرها.
/// </summary>
public class EmployeeCostRateService : IEmployeeCostRateService
{
    private const int MaxNotesLength = 300;

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly ISubscriptionEntitlements _entitlements;
    private readonly IAuditService _audit;
    private readonly IEmployeeService _employees;
    private readonly ITransactionRunner _tx;

    public EmployeeCostRateService(ErpDbContext db, ICurrentUser user, IPermissionService permissions, ISubscriptionEntitlements entitlements,
        IAuditService audit, IEmployeeService employees, ITransactionRunner tx)
    {
        _db = db; _user = user; _permissions = permissions; _entitlements = entitlements; _audit = audit; _employees = employees; _tx = tx;
    }

    public async Task<List<EmployeeCostRateDto>> ListAsync(string purpose, Guid? employeeId, CancellationToken ct = default)
    {
        var key = await AuthorizeAsync(purpose, ScreenAction.View, ct);
        var today = SaudiTime.Today;
        var rows = await (from r in _db.Set<EmployeeStandardCostRate>().AsNoTracking()
                          join e in _db.Set<Employee>().AsNoTracking() on r.EmployeeId equals e.Id
                          where r.Purpose == key && (employeeId == null || r.EmployeeId == employeeId)
                          orderby e.Code, r.EffectiveFrom descending
                          select new EmployeeCostRateDto
                          {
                              Id = r.Id, EmployeeId = r.EmployeeId, EmployeeCode = e.Code, EmployeeName = e.NameAr, EmployeeNameEn = e.NameEn,
                              Purpose = r.Purpose, CostPerHour = r.CostPerHour, EffectiveFrom = r.EffectiveFrom, Notes = r.Notes,
                              CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt,
                          }).ToListAsync(ct);
        // الساري اليوم: أحدث سطر لا يتجاوز اليوم لكل موظف
        foreach (var current in rows.Where(r => r.EffectiveFrom <= today).GroupBy(r => r.EmployeeId).Select(g => g.MaxBy(r => r.EffectiveFrom)!))
            current.IsCurrent = true;
        return rows;
    }

    public async Task<EmployeeCostRateDto> AddAsync(CreateEmployeeCostRateDto request, CancellationToken ct = default)
    {
        var key = await AuthorizeAsync(request.Purpose, ScreenAction.Edit, ct);
        var employee = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == request.EmployeeId)
                .Select(e => new { e.Code, e.NameAr, e.NameEn, e.Status })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.EmployeeCostRateEmployeeNotFound);
        if (employee.Status != EmployeeStatuses.Active) throw new ValidationFailedException(Messages.EmployeeCostRateEmployeeInactive);
        if (request.CostPerHour < 0) throw new ValidationFailedException(Messages.EmployeeCostRateNegative);

        var from = (request.EffectiveFrom ?? SaudiTime.Today).Date;
        if (await _db.Set<EmployeeStandardCostRate>().AnyAsync(r => r.EmployeeId == request.EmployeeId && r.Purpose == key && r.EffectiveFrom == from, ct))
            throw new ValidationFailedException(string.Format(Messages.EmployeeCostRateDuplicateDate, from));

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is { Length: > MaxNotesLength }) notes = notes[..MaxNotesLength];
        var rate = new EmployeeStandardCostRate
        {
            EmployeeId = request.EmployeeId, Purpose = key, CostPerHour = decimal.Round(request.CostPerHour, 4), EffectiveFrom = from, Notes = notes,
            CreatedBy = _user.Name ?? _user.Email, CreatedByUserId = _user.UserId,
        };
        _db.Add(rate);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("EMPLOYEE_COST_RATE_ADDED", nameof(EmployeeStandardCostRate), rate.Id.ToString(),
            $"{employee.Code} / {key}: {rate.CostPerHour:0.####} من {from:yyyy-MM-dd}", ct);

        var today = SaudiTime.Today;
        var latest = await _db.Set<EmployeeStandardCostRate>().AsNoTracking()
            .Where(r => r.EmployeeId == request.EmployeeId && r.Purpose == key && r.EffectiveFrom <= today)
            .OrderByDescending(r => r.EffectiveFrom).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        return new EmployeeCostRateDto
        {
            Id = rate.Id, EmployeeId = rate.EmployeeId, EmployeeCode = employee.Code, EmployeeName = employee.NameAr, EmployeeNameEn = employee.NameEn,
            Purpose = key, CostPerHour = rate.CostPerHour, EffectiveFrom = from, Notes = notes, CreatedBy = rate.CreatedBy, CreatedAt = rate.CreatedAt,
            IsCurrent = latest == rate.Id,
        };
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.Set<EmployeeStandardCostRate>().AsNoTracking().Where(r => r.Id == id)
                .Select(r => new { r.Id, r.TenantId, r.Purpose, r.EmployeeId, r.CostPerHour, r.EffectiveFrom })
                .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.EmployeeCostRateNotFound);
        await AuthorizeAsync(row.Purpose, ScreenAction.Edit, ct);
        _db.Remove(new EmployeeStandardCostRate { Id = row.Id, TenantId = row.TenantId });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("EMPLOYEE_COST_RATE_DELETED", nameof(EmployeeStandardCostRate), row.Id.ToString(),
            $"{row.EmployeeId} / {row.Purpose}: {row.CostPerHour:0.####} من {row.EffectiveFrom:yyyy-MM-dd}", ct);
    }

    public async Task<decimal?> RateAtAsync(Guid employeeId, string purpose, DateTime date, CancellationToken ct = default)
    {
        var day = date.Date;
        return await _db.Set<EmployeeStandardCostRate>().AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.Purpose == purpose && r.EffectiveFrom <= day)
            .OrderByDescending(r => r.EffectiveFrom)
            .Select(r => (decimal?)r.CostPerHour)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>الغرض معروف، وإضافته مرخّصة، والإجراء مسموح على شاشة إعداداتها؛ يعيد الغرض بصيغته المحفوظة.</summary>
    private async Task<string> AuthorizeAsync(string? purpose, ScreenAction action, CancellationToken ct)
    {
        var key = (purpose ?? string.Empty).Trim().ToLowerInvariant();
        var (module, screen) = key switch
        {
            EmployeeCostPurposes.Technician => (PlatformModules.Workshop, PermissionCatalog.WorkshopSettingsScreenId),
            EmployeeCostPurposes.Operator => (PlatformModules.EquipmentRental, PermissionCatalog.RentalSettingsScreenId),
            _ => throw new ValidationFailedException(Messages.EmployeeCostPurposeInvalid),
        };
        if (!(await _entitlements.GetAsync(ct)).Allows(module)) throw new ForbiddenException(Messages.AddOnNotLicensed);
        await _permissions.DemandAsync(screen, action, ct: ct);
        return key;
    }
}
