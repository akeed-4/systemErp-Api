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

    public async Task<List<AddOnStaffDto>> StaffAsync(string purpose, CancellationToken ct = default)
    {
        var key = await AuthorizeAsync(purpose, ScreenAction.View, ct, allowDocumentScreen: true);
        var today = SaudiTime.Today;
        var staff = await _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.Code)
            .Select(e => new AddOnStaffDto { EmployeeId = e.Id, Code = e.Code, NameAr = e.NameAr, NameEn = e.NameEn, Phone = e.Phone, JobTitle = e.JobTitle })
            .ToListAsync(ct);
        var rates = (await _db.Set<EmployeeStandardCostRate>().AsNoTracking().Where(r => r.Purpose == key && r.EffectiveFrom <= today)
                .Select(r => new { r.EmployeeId, r.EffectiveFrom, r.CostPerHour }).ToListAsync(ct))
            .GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(r => r.EffectiveFrom)!.CostPerHour);
        foreach (var s in staff) s.CurrentCostPerHour = rates.TryGetValue(s.EmployeeId, out var rate) ? rate : null;
        return staff;
    }

    public Task<AddOnStaffDto> RegisterStaffAsync(RegisterAddOnStaffDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var key = await AuthorizeAsync(request.Purpose, ScreenAction.Edit, token);
            if (request.CostPerHour is < 0) throw new ValidationFailedException(Messages.EmployeeCostRateNegative);
            var hire = (request.HireDate ?? SaudiTime.Today).Date;
            var code = string.IsNullOrWhiteSpace(request.Code) ? await NextCodeAsync(key, token) : request.Code.Trim();
            // ملف الموظف نفسه بقواعد خدمة الموظفين (لا جدول ثانٍ)؛ ما لا يُدخل هنا يكمله شؤون الموظفين لاحقاً
            var employee = await _employees.CreateAsync(new CreateEmployeeDto
            {
                Code = code, NameAr = request.NameAr?.Trim() ?? string.Empty, NameEn = Clean(request.NameEn), Phone = Clean(request.Phone),
                NationalId = Clean(request.NationalId), IdType = string.IsNullOrWhiteSpace(request.IdType) ? EmployeeIdTypes.NationalId : request.IdType.Trim(),
                Nationality = Clean(request.Nationality), JobTitle = Clean(request.JobTitle) ?? DefaultJobTitle(key), HireDate = hire,
                BasicSalary = request.BasicSalary, Status = EmployeeStatuses.Active,
                Notes = key == EmployeeCostPurposes.Technician ? "سُجِّل من إعدادات الورش" : "سُجِّل من إعدادات التأجير",
            }, token);
            await _audit.LogAsync("ADDON_STAFF_REGISTERED", nameof(Employee), employee.Id.ToString(), $"{employee.Code} / {key}", token);
            if (request.CostPerHour is decimal cost)
                await AddAsync(new CreateEmployeeCostRateDto { EmployeeId = employee.Id, Purpose = key, CostPerHour = cost, EffectiveFrom = hire }, token);
            return new AddOnStaffDto
            {
                EmployeeId = employee.Id, Code = employee.Code, NameAr = employee.NameAr, NameEn = employee.NameEn, Phone = employee.Phone,
                JobTitle = employee.JobTitle, CurrentCostPerHour = request.CostPerHour is decimal c && hire <= SaudiTime.Today ? decimal.Round(c, 4) : null,
            };
        }, ct);

    private async Task<string> NextCodeAsync(string purpose, CancellationToken ct)
    {
        var prefix = purpose == EmployeeCostPurposes.Technician ? "TEC-" : "OPR-";
        var n = await _db.Set<Employee>().CountAsync(e => e.Code.StartsWith(prefix), ct) + 1;
        while (await _db.Set<Employee>().AnyAsync(e => e.Code == prefix + n.ToString("0000"), ct)) n++;
        return prefix + n.ToString("0000");
    }

    private static string DefaultJobTitle(string purpose) => purpose == EmployeeCostPurposes.Technician ? "فني" : "مشغّل معدات";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>الغرض معروف، وإضافته مرخّصة، والإجراء مسموح على شاشة إعداداتها؛ يعيد الغرض بصيغته المحفوظة.</summary>
    /// <param name="allowDocumentScreen">العرض يكفيه أيضاً شاشة مستندات الإضافة (أوامر العمل أو عقود التأجير) لاختيار الموظف فيها.</param>
    private async Task<string> AuthorizeAsync(string? purpose, ScreenAction action, CancellationToken ct, bool allowDocumentScreen = false)
    {
        var key = (purpose ?? string.Empty).Trim().ToLowerInvariant();
        var (module, screen, documents) = key switch
        {
            EmployeeCostPurposes.Technician => (PlatformModules.Workshop, PermissionCatalog.WorkshopSettingsScreenId, PermissionCatalog.WorkshopOrdersScreenId),
            EmployeeCostPurposes.Operator => (PlatformModules.EquipmentRental, PermissionCatalog.RentalSettingsScreenId, PermissionCatalog.RentalContractsScreenId),
            _ => throw new ValidationFailedException(Messages.EmployeeCostPurposeInvalid),
        };
        if (!(await _entitlements.GetAsync(ct)).Allows(module)) throw new ForbiddenException(Messages.AddOnNotLicensed);
        if (allowDocumentScreen && await _permissions.HasPermissionAsync(documents, action, ct)) return key;
        await _permissions.DemandAsync(screen, action, ct: ct);
        return key;
    }
}
