using ERP.Service.Data;
using A = ERP.Core.DTOs.Shared.ScreenAction;

namespace ERP.Service.Services.Shared;

/// <summary>الدور الأساسي (افتراضي بلا سجل في RoleDefinition إلا عند تعطيله أو تعديل وصفه).</summary>
public sealed record SystemRole(string Code, UserRole Role, string NameAr, string NameEn);

/// <summary>من تُحسب له الصلاحيات: المستخدم ودوره الفعلي (المخصّص إن وُجد وإلا الأساسي).</summary>
public sealed class PermissionSubject
{
    public Guid? UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public string RoleCode { get; init; } = string.Empty;
    public string RoleNameAr { get; init; } = string.Empty;
    public string RoleNameEn { get; init; } = string.Empty;
    public bool RoleActive { get; init; } = true;
    public string? Department { get; init; }
    public bool FullAccess => PermissionStore.IsFullAccess(RoleCode);
}

/// <summary>صلاحيات شاشة محسوبة: الإجراءات الممنوحة، والأعمدة الخمسة، وهل هي من سجل قديم/افتراضي (تُفحص بأعمدتها).</summary>
public sealed class ScreenGrant
{
    public required PermissionScreenDefinition Screen { get; init; }
    public required HashSet<A> Actions { get; init; }
    /// <summary>View, Create, Edit, Delete, Approve.</summary>
    public required bool[] Columns { get; init; }
    public bool Legacy { get; init; }
}

public sealed class PermissionGrants
{
    public bool FullAccess { get; init; }
    public Dictionary<string, ScreenGrant> Screens { get; init; } = new();
    public PermissionSettingsDto Settings { get; init; } = new();
    /// <summary>user | role | default</summary>
    public string Source { get; init; } = "default";

    public bool Allows(string screenId, A action, A? legacy = null)
    {
        if (FullAccess) return PermissionCatalog.Find(screenId) != null;
        if (!Screens.TryGetValue(screenId, out var g)) return false;
        if (legacy.HasValue && g.Legacy) return g.Columns[PermissionStore.Column(legacy.Value)];
        if (g.Screen.Actions.Contains(action)) return g.Actions.Contains(action);
        return g.Columns[PermissionStore.Column(PermissionCatalog.LegacySource(screenId, action))];
    }

    public int Count => FullAccess ? PermissionCatalog.TotalPermissions : Screens.Values.Sum(s => s.Actions.Count);
}

/// <summary>
/// قراءة الصلاحيات المحفوظة وحسابها (للخدمتين PermissionService وRoleService): سجل المستخدم يتفوق على سجل دوره،
/// وما لم يُحفظ لشاشة يُؤخذ من افتراضي الدور. السجل القديم (بلا Actions) تُشتق إجراءاته من أعمدته الخمسة.
/// </summary>
public sealed class PermissionStore
{
    public static readonly SystemRole[] SystemRoles =
    {
        new("owner", UserRole.Owner, "المالك", "Owner"),
        new("admin", UserRole.Admin, "مدير النظام", "Administrator"),
        new("general_manager", UserRole.GeneralManager, "المدير العام", "General Manager"),
        new("chief_accountant", UserRole.ChiefAccountant, "رئيس الحسابات", "Chief Accountant"),
        new("sales_rep", UserRole.SalesRep, "مندوب مبيعات", "Sales Representative"),
    };

    public static SystemRole? FindSystem(string? code) => SystemRoles.FirstOrDefault(r => r.Code == code);
    public static bool IsFullAccess(string? code) => code is "owner" or "admin";

    public static int Column(A legacy) => legacy switch
    {
        A.View => 0, A.Create => 1, A.Edit => 2, A.Delete => 3, A.Approve => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(legacy)),
    };

    private readonly ErpDbContext _db;
    public PermissionStore(ErpDbContext db) => _db = db;

    // ---------- القراءة ----------

    public Task<UserRolePermission?> RoleRowAsync(string roleCode, CancellationToken ct, bool track = false)
        => Rows(track).FirstOrDefaultAsync(p => p.RoleId == roleCode && p.UserId == null, ct);

    /// <summary>تخصيص المستخدم المباشر؛ السجل الذي لا يحمل إلا منحة المنصة ليس تخصيصاً.</summary>
    public async Task<UserRolePermission?> UserRowAsync(Guid userId, CancellationToken ct, bool track = false, bool includePlatformOnly = false)
    {
        var row = await Rows(track).FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return row == null || includePlatformOnly || HasRegular(row) ? row : null;
    }

    public static bool HasRegular(UserRolePermission row) => row.Permissions.Any(p => p.ScreenId != PermissionService.PlatformScreenId);

    private IQueryable<UserRolePermission> Rows(bool track)
    {
        var q = _db.Set<UserRolePermission>().Include(p => p.Permissions);
        return track ? q : q.AsNoTracking();
    }

    public async Task<PermissionSubject?> SubjectAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Name, u.IsActive, u.Role, u.CustomRoleId, u.Department }).FirstOrDefaultAsync(ct);
        if (user == null) return null;

        RoleDefinition? custom = user.CustomRoleId.HasValue
            ? await _db.Set<RoleDefinition>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.CustomRoleId, ct)
            : null;
        var code = custom?.Code ?? RoleIds.From(user.Role);
        var (nameAr, nameEn, active) = custom != null ? (custom.NameAr, custom.NameEn, custom.IsActive) : await RoleInfoAsync(code, ct);
        return new PermissionSubject
        {
            UserId = userId, Name = user.Name, IsActive = user.IsActive, RoleCode = code, RoleNameAr = nameAr, RoleNameEn = nameEn,
            RoleActive = active, Department = string.IsNullOrWhiteSpace(user.Department) ? null : user.Department.Trim(),
        };
    }

    /// <summary>اسم الدور وحالته (أساسي أو مخصّص)، أو يرمي إن لم يكن معروفاً.</summary>
    public async Task<(string NameAr, string NameEn, bool Active)> RoleInfoAsync(string code, CancellationToken ct)
    {
        var def = await _db.Set<RoleDefinition>().AsNoTracking().FirstOrDefaultAsync(r => r.Code == code, ct);
        var system = FindSystem(code);
        if (system != null) return (def?.NameAr is { Length: > 0 } ar ? ar : system.NameAr, def?.NameEn is { Length: > 0 } en ? en : system.NameEn, def?.IsActive ?? true);
        if (code == PermissionService.EmployeeRoleId) return ("موظف", "Employee", true);
        if (def == null) throw new NotFoundException(string.Format(Messages.UnknownRole, code));
        return (def.NameAr, def.NameEn, def.IsActive);
    }

    /// <summary>صلاحيات المستخدم الفعلية: تخصيصه المباشر إن وُجد، وإلا صلاحيات دوره.</summary>
    public async Task<PermissionGrants> GrantsForAsync(PermissionSubject subject, CancellationToken ct)
    {
        if (subject.FullAccess) return Build(null, subject.RoleCode);
        var row = subject.UserId.HasValue ? await UserRowAsync(subject.UserId.Value, ct) : null;
        if (row != null) return Build(row, subject.RoleCode, "user");
        return await RoleGrantsAsync(subject.RoleCode, ct);
    }

    public async Task<PermissionGrants> RoleGrantsAsync(string roleCode, CancellationToken ct)
    {
        if (IsFullAccess(roleCode)) return Build(null, roleCode);
        var row = await RoleRowAsync(roleCode, ct);
        return Build(row, roleCode, row != null ? "role" : "default");
    }

    /// <summary>حساب الصلاحيات من سجل (أو افتراضي الدور إن كان null).</summary>
    public static PermissionGrants Build(UserRolePermission? row, string roleCode, string? source = null)
    {
        if (IsFullAccess(roleCode))
            return new PermissionGrants { FullAccess = true, Source = "default" };

        var items = (row?.Permissions ?? new List<ScreenPermissionItem>())
            .Where(p => p.ScreenId != PermissionService.PlatformScreenId)
            .GroupBy(p => p.ScreenId).ToDictionary(g => g.Key, g => g.First());
        var defaults = DefaultColumns(roleCode);
        var screens = new Dictionary<string, ScreenGrant>();
        foreach (var screen in PermissionCatalog.Screens)
        {
            if (items.TryGetValue(screen.Id, out var item) && item.Actions != null)
            {
                var granted = ParseActions(item.Actions).Where(screen.Actions.Contains).ToHashSet();
                screens[screen.Id] = new ScreenGrant { Screen = screen, Actions = granted, Columns = ColumnsOf(granted) };
                continue;
            }
            // شاشة فُصلت من أصل ولم تُحفظ لها صلاحيات: تُشتق من أعمدة الأصل (كما كانت محميّة به)
            var parent = item == null && screen.InheritsFrom != null ? items.GetValueOrDefault(screen.InheritsFrom) : null;
            var source0 = item ?? parent;
            var columns = source0 != null
                ? new[] { source0.CanView, source0.CanCreate, source0.CanEdit, source0.CanDelete, source0.CanApprove }
                : defaults[screen.Id];
            screens[screen.Id] = new ScreenGrant
            {
                Screen = screen, Columns = columns, Legacy = true,
                Actions = screen.Actions.Where(a => columns[Column(PermissionCatalog.LegacySource(screen.Id, a))]).ToHashSet(),
            };
        }

        return new PermissionGrants
        {
            Screens = screens, Source = source ?? "default",
            Settings = row == null ? new PermissionSettingsDto() : SettingsOf(row),
        };
    }

    public static PermissionSettingsDto SettingsOf(UserRolePermission row) => new()
    {
        Scope = row.Scope,
        WarehouseIds = ParseGuids(row.ScopeWarehouseIds),
        ApprovalLimit = row.ApprovalLimit,
    };

    public static bool[] ColumnsOf(ICollection<A> actions)
        => new[] { actions.Contains(A.View), actions.Contains(A.Create), actions.Contains(A.Edit), actions.Contains(A.Delete), actions.Contains(A.Approve) };

    public static IEnumerable<A> ParseActions(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) yield break;
        foreach (var code in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (PermissionCatalog.TryParse(code, out var a)) yield return a;
    }

    public static string FormatActions(IEnumerable<A> actions)
        => string.Join(",", actions.Distinct().OrderBy(a => (int)a).Select(PermissionCatalog.Code));

    public static List<Guid> ParseGuids(string? csv)
        => (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();

    /// <summary>
    /// الافتراضي لكل دور (كما كان قبل الكتالوج): الكل للمالك والمدير، المندوب بلا حذف/اعتماد ولا حسابات وتقارير،
    /// والصلاحيات الحسّاسة للأدوار الإدارية فقط. الأدوار المخصّصة بلا صلاحيات افتراضية.
    /// </summary>
    public static Dictionary<string, bool[]> DefaultColumns(string role)
    {
        var known = FindSystem(role) != null;
        var result = new Dictionary<string, bool[]>();
        foreach (var s in PermissionCatalog.Screens)
        {
            var id = s.InheritsFrom ?? s.Id;
            bool view = known, create = known, edit = known, delete = known, approve = known;
            if (role == PermissionService.EmployeeRoleId || !known) view = create = edit = delete = approve = false;
            else if (s.Restricted) view = create = edit = delete = approve = PermissionService.PermissionAdminRoles.Contains(role);
            else if (role == "sales_rep")
            {
                approve = false; delete = false;
                if (id is "accounts" or "hr" or "approval-policies" or "user-permissions" or "reports")
                    view = create = edit = false;
            }
            else if (role is "chief_accountant" or "general_manager")
            {
                if (id == "user-permissions") { view = create = edit = approve = true; delete = false; }
            }
            else if (role is not ("owner" or "admin"))
            {
                if (id == "user-permissions") view = create = edit = delete = approve = false;
            }
            result[s.Id] = new[] { view, create, edit, delete, approve };
        }
        return result;
    }

    // ---------- سجل التغييرات ----------

    /// <summary>فروق الصلاحيات والإعدادات بين حالتين كسطور سجل (لا تُحفظ هنا).</summary>
    public static List<PermissionChangeLog> Diff(PermissionGrants before, PermissionGrants after, Func<string, string?, string?, PermissionChangeLog> entry)
    {
        var logs = new List<PermissionChangeLog>();
        foreach (var screen in PermissionCatalog.Screens)
            foreach (var action in screen.Actions)
            {
                bool was = before.Allows(screen.Id, action), now = after.Allows(screen.Id, action);
                if (was != now) logs.Add(entry($"{screen.Id}.{PermissionCatalog.Code(action)}", Flag(was), Flag(now)));
            }
        var (b, a) = (before.Settings, after.Settings);
        if (b.Scope != a.Scope) logs.Add(entry("scope", b.Scope.ToString(), a.Scope.ToString()));
        var bw = string.Join(",", b.WarehouseIds.OrderBy(x => x));
        var aw = string.Join(",", a.WarehouseIds.OrderBy(x => x));
        if (bw != aw) logs.Add(entry("scope.warehouses", Empty(bw), Empty(aw)));
        if (b.ApprovalLimit != a.ApprovalLimit) logs.Add(entry("approval_limit", b.ApprovalLimit?.ToString("0.##"), a.ApprovalLimit?.ToString("0.##")));
        return logs;
    }

    private static string Flag(bool v) => v ? "granted" : "denied";
    private static string? Empty(string s) => s.Length == 0 ? null : s;
}
