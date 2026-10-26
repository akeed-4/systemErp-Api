using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// أدوار المنشأة: الأساسية افتراضية (سجل RoleDefinition لها فقط عند تعطيلها أو تعديل وصفها)، والمخصّصة سجلات كاملة
/// صلاحياتها في UserRolePermission بـ RoleId = Code. كل تغيير يُسجَّل في سجل تغييرات الصلاحيات.
/// </summary>
public class RoleService : IRoleService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly PermissionStore _store;

    public RoleService(ErpDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
        _store = new PermissionStore(db);
    }

    public async Task<List<RoleDto>> ListAsync(CancellationToken ct = default)
    {
        var defs = await _db.Set<RoleDefinition>().AsNoTracking().ToListAsync(ct);
        var rows = await _db.Set<UserRolePermission>().AsNoTracking().Include(p => p.Permissions)
            .Where(p => p.UserId == null && p.RoleId != null).ToListAsync(ct);
        var users = await _db.Set<User>().AsNoTracking().Where(u => u.Role != UserRole.Employee)
            .Select(u => new { u.Role, u.CustomRoleId }).ToListAsync(ct);
        var lastChanges = (await _db.Set<PermissionChangeLog>().AsNoTracking().Where(l => l.TargetType == "role")
                .GroupBy(l => l.TargetId).Select(g => g.OrderByDescending(l => l.Timestamp).First()).ToListAsync(ct))
            .ToDictionary(l => l.TargetId);

        RoleDto Map(string code, RoleDefinition? def, SystemRole? system)
        {
            var row = rows.FirstOrDefault(r => r.RoleId == code);
            var grants = PermissionStore.Build(row, code);
            var last = lastChanges.GetValueOrDefault(code);
            var stamps = new[] { def?.UpdatedAt ?? def?.CreatedAt, row?.UpdatedAt ?? row?.CreatedAt, last?.Timestamp }.Where(d => d.HasValue).ToList();
            return new RoleDto
            {
                Id = system == null ? def?.Id : null,
                Code = code,
                NameAr = def?.NameAr is { Length: > 0 } ar ? ar : system?.NameAr ?? code,
                NameEn = def?.NameEn is { Length: > 0 } en ? en : system?.NameEn ?? code,
                Description = def?.Description,
                BaseRole = system?.Role ?? def!.BaseRole,
                IsSystem = system != null,
                IsActive = def?.IsActive ?? true,
                FullAccess = grants.FullAccess,
                UsersCount = system != null
                    ? users.Count(u => u.Role == system.Role && u.CustomRoleId == null)
                    : users.Count(u => u.CustomRoleId == def!.Id),
                PermissionsCount = grants.Count,
                TotalPermissions = PermissionCatalog.TotalPermissions,
                Settings = grants.Settings,
                UpdatedAt = stamps.Count > 0 ? stamps.Max() : null,
                UpdatedBy = last?.ChangedByName ?? def?.UpdatedBy,
            };
        }

        var list = PermissionStore.SystemRoles.Select(s => Map(s.Code, defs.FirstOrDefault(d => d.Code == s.Code), s)).ToList();
        list.AddRange(defs.Where(d => !d.IsSystem).OrderBy(d => d.CreatedAt).Select(d => Map(d.Code, d, null)));
        return list;
    }

    public async Task<RoleDto> GetAsync(string code, CancellationToken ct = default)
        => (await ListAsync(ct)).FirstOrDefault(r => r.Code == code) ?? throw new NotFoundException(Messages.RoleNotFound);

    public async Task<RoleDto> CreateAsync(SaveRoleDto dto, CancellationToken ct = default)
    {
        var def = await NewRoleAsync(dto, ct);
        _db.Add(Log(def.Code, def.NameAr, "role.create", null, def.NameAr));
        await _db.SaveChangesAsync(ct);
        return await GetAsync(def.Code, ct);
    }

    public async Task<RoleDto> CopyAsync(string sourceCode, SaveRoleDto dto, CancellationToken ct = default)
    {
        var (sourceName, _, _) = await _store.RoleInfoAsync(sourceCode, ct);
        var source = await _store.RoleGrantsAsync(sourceCode, ct);
        var def = await NewRoleAsync(dto, ct);

        // نسخة صريحة بالإجراءات (لا بالأعمدة) فتبقى مستقلة عن افتراضيات الأصل وتعديلاته اللاحقة
        var row = new UserRolePermission
        {
            RoleId = def.Code, Scope = source.Settings.Scope, ApprovalLimit = source.Settings.ApprovalLimit,
            ScopeWarehouseIds = source.Settings.WarehouseIds.Count > 0 ? string.Join(",", source.Settings.WarehouseIds) : null,
        };
        foreach (var screen in PermissionCatalog.Screens)
        {
            var granted = screen.Actions.Where(a => source.Allows(screen.Id, a)).ToHashSet();
            var cols = PermissionStore.ColumnsOf(granted);
            row.Permissions.Add(new ScreenPermissionItem
            {
                ScreenId = screen.Id, CanView = cols[0], CanCreate = cols[1], CanEdit = cols[2], CanDelete = cols[3], CanApprove = cols[4],
                Actions = PermissionStore.FormatActions(granted),
            });
        }
        _db.Add(row);
        _db.Add(Log(def.Code, def.NameAr, "role.copy", sourceName, def.NameAr));
        await _db.SaveChangesAsync(ct);
        return await GetAsync(def.Code, ct);
    }

    public async Task<RoleDto> UpdateAsync(string code, SaveRoleDto dto, CancellationToken ct = default)
    {
        var def = await DefinitionAsync(code, ct);
        var system = PermissionStore.FindSystem(code);
        var oldName = def.NameAr;
        if (system == null)
        {
            // الأساسي يبقى باسمه المعروف؛ المخصّص يُسمّى من جديد
            var (ar, en) = await ValidNamesAsync(dto, def.Id, ct);
            def.NameAr = ar; def.NameEn = en;
        }
        def.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        def.UpdatedBy = _user.Name;
        if (oldName != def.NameAr) _db.Add(Log(code, def.NameAr, "role.name", oldName, def.NameAr));
        await _db.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    public async Task<RoleDto> SetActiveAsync(string code, bool active, CancellationToken ct = default)
    {
        var def = await DefinitionAsync(code, ct);
        if (def.IsActive != active)
        {
            def.IsActive = active;
            def.UpdatedBy = _user.Name;
            _db.Add(Log(code, def.NameAr, "role.status", active ? "inactive" : "active", active ? "active" : "inactive"));
            await _db.SaveChangesAsync(ct);
        }
        return await GetAsync(code, ct);
    }

    /// <summary>سجل الدور للتعديل؛ يُنشأ للدور الأساسي عند أول تعديل. المالك والمدير لا يُعدَّلان.</summary>
    private async Task<RoleDefinition> DefinitionAsync(string code, CancellationToken ct)
    {
        if (PermissionStore.IsFullAccess(code)) throw new ConflictException(Messages.FullAccessRoleLocked);
        var def = await _db.Set<RoleDefinition>().FirstOrDefaultAsync(r => r.Code == code, ct);
        if (def != null) return def;
        var system = PermissionStore.FindSystem(code) ?? throw new NotFoundException(Messages.RoleNotFound);
        def = new RoleDefinition { Code = code, NameAr = system.NameAr, NameEn = system.NameEn, BaseRole = system.Role, IsSystem = true };
        _db.Add(def);
        return def;
    }

    private async Task<RoleDefinition> NewRoleAsync(SaveRoleDto dto, CancellationToken ct)
    {
        var (ar, en) = await ValidNamesAsync(dto, null, ct);
        var def = new RoleDefinition
        {
            Code = $"role_{Guid.NewGuid():N}"[..17], NameAr = ar, NameEn = en, BaseRole = UserRole.SalesRep,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(), UpdatedBy = _user.Name,
        };
        _db.Add(def);
        return def;
    }

    private async Task<(string Ar, string En)> ValidNamesAsync(SaveRoleDto dto, Guid? selfId, CancellationToken ct)
    {
        var ar = dto.NameAr?.Trim() ?? string.Empty;
        if (ar.Length == 0) throw new ValidationFailedException(Messages.RoleNameRequired);
        var en = string.IsNullOrWhiteSpace(dto.NameEn) ? ar : dto.NameEn.Trim();
        var taken = PermissionStore.SystemRoles.Any(s => s.NameAr == ar)
            || await _db.Set<RoleDefinition>().AnyAsync(r => r.NameAr == ar && r.Id != selfId, ct);
        if (taken) throw new ConflictException(Messages.RoleNameDuplicate);
        return (ar, en);
    }

    private PermissionChangeLog Log(string code, string name, string permission, string? old, string? now) => new()
    {
        ChangedByUserId = _user.UserId, ChangedByName = _user.Name ?? "system", TargetType = "role", TargetId = code,
        TargetName = name, Permission = permission, OldValue = old, NewValue = now, Timestamp = DateTime.UtcNow,
    };
}
