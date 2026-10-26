using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using A = ERP.Core.DTOs.Shared.ScreenAction;

namespace ERP.Service.Services.Shared;

/// <summary>
/// نظام الصلاحيات الموحّد للنظم الثلاثة (محاسبة، معرض سيارات، POS): الكتالوج في <see cref="PermissionCatalog"/>،
/// والقراءة والحساب في <see cref="PermissionStore"/>. الفحص الكامل لسجل محدد = صلاحية + نطاق + مرحلة + حد اعتماد،
/// ويُستخدم نفسه في الحماية (403 بسبب الرفض) وفي شاشة فحص الصلاحية. المالك والمدير صلاحيات كاملة.
/// </summary>
public class PermissionService : IPermissionService
{
    public const string CarCostViewScreenId = "car-cost-view";
    public const string ChargeTreatmentOverrideScreenId = "charge-treatment-override";
    public const string LandedCostApprovalScreenId = "landed-cost-approval";
    public const string SoldVehicleAdjustmentScreenId = "sold-vehicle-adjustment";
    public const string ItemStandardCostScreenId = "item-standard-cost-revaluation";
    public const string InvoiceTaxTreatmentOverrideScreenId = "invoice-tax-treatment-override";

    /// <summary>دور بوابة الموظف (UserRole.Employee): لا يملك أي شاشة، ويُحصر في مسارات الخدمة الذاتية.</summary>
    public const string EmployeeRoleId = "employee";

    /// <summary>أدوار تدير الصلاحيات دائماً (لا يحجبها عنها حفظ خاطئ) وتنال الصلاحيات الحسّاسة افتراضياً.</summary>
    public static readonly HashSet<string> PermissionAdminRoles = new() { "owner", "admin", "chief_accountant", "general_manager" };

    /// <summary>
    /// صلاحية إدارة المنصة (كل المنشآت). ليست ضمن الكتالوج عمداً: لا افتراضيات لها ولا يملكها المالك/المدير
    /// تلقائياً، وتُمنح لمستخدم محدد بالاسم ولا يمنحها أو يراها إلا مدير منصة. العرض = فتح اللوحة، التعديل = إدارتها.
    /// </summary>
    public const string PlatformScreenId = "platform-admin";
    private const string PlatformNameAr = "إدارة المنصة (اشتراكات كل الشركات والباقات)";
    private const string PlatformNameEn = "Platform Administration (all companies & plans)";

    /// <summary>إجراءات القراءة: لا تتقيّد بمرحلة السجل.</summary>
    private static readonly HashSet<A> ReadActions = new() { A.View, A.Print, A.Export, A.Pdf };

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPlatformAccessService _platform;
    private readonly PermissionStore _store;

    // صلاحيات المستخدم الحالي محسوبة مرة واحدة لكل طلب
    private (PermissionSubject? Subject, PermissionGrants Grants)? _mine;

    public PermissionService(ErpDbContext db, ICurrentUser user, IPlatformAccessService platform)
    {
        _db = db; _user = user; _platform = platform;
        _store = new PermissionStore(db);
    }

    // ---------- الكتالوج ----------

    public List<ScreenDefinitionDto> GetScreens()
        => PermissionCatalog.Screens.Select(s => new ScreenDefinitionDto { Id = s.Id, NameAr = s.NameAr, NameEn = s.NameEn }).ToList();

    public PermissionCatalogDto GetCatalog() => new()
    {
        Actions = PermissionCatalog.Actions.Select(a => new PermissionActionDto
        {
            Code = a.Code, Group = a.Group.ToString().ToLowerInvariant(), NameAr = a.NameAr, NameEn = a.NameEn,
        }).ToList(),
        Modules = PermissionCatalog.Modules.Select(m => new PermissionModuleDto
        {
            Id = m.Id, NameAr = m.NameAr, NameEn = m.NameEn, Icon = m.Icon,
            Screens = PermissionCatalog.Screens.Where(s => s.ModuleId == m.Id).Select(s => new PermissionScreenDto
            {
                Id = s.Id, ModuleId = s.ModuleId, NameAr = s.NameAr, NameEn = s.NameEn, Restricted = s.Restricted,
                Actions = s.Actions.Select(PermissionCatalog.Code).ToList(), Routes = s.Routes.ToList(),
            }).ToList(),
        }).ToList(),
        RecordTypes = ScopedRecordTypes.All.Select(t => new PermissionRecordTypeDto
        {
            Key = t.Key, ScreenId = t.ScreenId, NameAr = t.NameAr, NameEn = t.NameEn, HasStages = t.HasStages,
        }).ToList(),
        TotalPermissions = PermissionCatalog.TotalPermissions,
    };

    // ---------- القراءة والتعديل ----------

    public async Task<List<ScreenPermissionDto>> GetForUserOrRoleAsync(Guid? userId, string? roleId, CancellationToken ct = default)
        => ToDtos((await TargetGrantsAsync(userId, roleId, ct)).Grants);

    /// <summary>صلاحيات المستخدم (تخصيصه أو دوره) أو الدور، والدور المعتمد.</summary>
    private async Task<(PermissionGrants Grants, string RoleCode, PermissionSubject? Subject)> TargetGrantsAsync(Guid? userId, string? roleId, CancellationToken ct)
    {
        if (userId.HasValue)
        {
            var subject = await _store.SubjectAsync(userId.Value, ct) ?? throw new NotFoundException(Messages.UserNotFound);
            var code = string.IsNullOrEmpty(roleId) ? subject.RoleCode : roleId;
            var row = await _store.UserRowAsync(userId.Value, ct);
            return (row != null && !PermissionStore.IsFullAccess(code) ? PermissionStore.Build(row, code, "user") : await _store.RoleGrantsAsync(code, ct), code, subject);
        }
        var role = string.IsNullOrEmpty(roleId) ? "owner" : roleId;
        return (await _store.RoleGrantsAsync(role, ct), role, null);
    }

    private static List<ScreenPermissionDto> ToDtos(PermissionGrants grants)
        => PermissionCatalog.Screens.Select(s =>
        {
            var g = grants.Screens.GetValueOrDefault(s.Id);
            var actions = grants.FullAccess ? s.Actions.ToHashSet() : g?.Actions ?? new HashSet<A>();
            var cols = grants.FullAccess ? new[] { true, true, true, true, true } : g?.Columns ?? new bool[5];
            return new ScreenPermissionDto
            {
                ScreenId = s.Id, ScreenNameAr = s.NameAr, ScreenNameEn = s.NameEn,
                CanView = cols[0], CanCreate = cols[1], CanEdit = cols[2], CanDelete = cols[3], CanApprove = cols[4],
                Actions = s.Actions.Where(actions.Contains).Select(PermissionCatalog.Code).ToList(),
            };
        }).ToList();

    public async Task<List<ScreenPermissionDto>> GetForEditingAsync(Guid? userId, string? roleId, CancellationToken ct = default)
    {
        var list = await GetForUserOrRoleAsync(userId, roleId, ct);
        if (!userId.HasValue || !(await _platform.GetAsync(ct)).CanManage) return list;

        var grant = await _db.Set<UserRolePermission>().AsNoTracking()
            .Where(p => p.UserId == userId)
            .SelectMany(p => p.Permissions)
            .Where(i => i.ScreenId == PlatformScreenId)
            .Select(i => new { i.CanView, i.CanEdit })
            .FirstOrDefaultAsync(ct);
        list.Add(new ScreenPermissionDto
        {
            ScreenId = PlatformScreenId, ScreenNameAr = PlatformNameAr, ScreenNameEn = PlatformNameEn,
            CanView = grant?.CanView ?? false, CanEdit = grant?.CanEdit ?? false,
        });
        return list;
    }

    public async Task<PermissionSettingsDto> GetSettingsAsync(Guid? userId, string? roleId, CancellationToken ct = default)
        => (await TargetGrantsAsync(userId, roleId, ct)).Grants.Settings;

    public async Task SaveAsync(SavePermissionsRequestDto request, CancellationToken ct = default)
    {
        if (request.UserId == null && string.IsNullOrEmpty(request.RoleId))
            throw new ValidationFailedException(Messages.SpecifyUserOrRole);

        // صلاحية المنصة: يمنحها أو يسحبها مدير منصة فقط، ولمستخدم محدد لا لدور. مدير أي منشأة لا يستطيع إرسالها.
        var platformGrant = request.Permissions.FirstOrDefault(p => p.ScreenId == PlatformScreenId);
        if (platformGrant != null)
        {
            if (!(await _platform.GetAsync(ct)).CanManage) throw new ForbiddenException();
            if (request.UserId == null) throw new ValidationFailedException(Messages.PlatformPermissionUserOnly);
        }
        var regular = request.Permissions.Where(p => p.ScreenId != PlatformScreenId).ToList();

        var unknown = regular.Select(p => p.ScreenId).Where(id => PermissionCatalog.Find(id) == null).Distinct().ToList();
        if (unknown.Count > 0) throw new ValidationFailedException(Messages.UnknownScreensPrefix + string.Join(", ", unknown));
        var badActions = regular.SelectMany(p => p.Actions ?? new List<string>()).Where(c => !PermissionCatalog.TryParse(c, out _)).Distinct().ToList();
        if (badActions.Count > 0) throw new ValidationFailedException(string.Format(Messages.UnknownPermissionAction, string.Join(", ", badActions)));
        if (request.Settings?.ApprovalLimit < 0) throw new ValidationFailedException(Messages.NegativeApprovalLimit);

        // الهدف: مستخدم (ودوره لافتراضياته) أو دور معروف لا صلاحيات كاملة له
        PermissionSubject? subject = null;
        string roleCode, targetName;
        if (request.UserId.HasValue)
        {
            subject = await _store.SubjectAsync(request.UserId.Value, ct) ?? throw new NotFoundException(Messages.UserNotFound);
            roleCode = subject.RoleCode; targetName = subject.Name;
        }
        else
        {
            roleCode = request.RoleId!;
            if (PermissionStore.IsFullAccess(roleCode) || roleCode == EmployeeRoleId) throw new ConflictException(Messages.FullAccessRoleLocked);
            targetName = (await _store.RoleInfoAsync(roleCode, ct)).NameAr;
        }

        var existing = request.UserId.HasValue
            ? await _store.UserRowAsync(request.UserId.Value, ct, track: true, includePlatformOnly: true)
            : await _store.RoleRowAsync(roleCode, ct, track: true);

        // الحالة قبل الحفظ للسجل: تخصيص المستخدم القائم، أو صلاحيات دوره
        var before = existing != null && PermissionStore.HasRegular(existing)
            ? PermissionStore.Build(existing, roleCode)
            : await _store.RoleGrantsAsync(roleCode, ct);

        // منحة المنصة القائمة تبقى كما هي إن لم يرسلها مدير منصة (حفظ مدير المنشأة لباقي الصلاحيات لا يسحبها)
        (bool View, bool Edit)? platform = null;
        if (existing == null)
        {
            existing = new UserRolePermission { UserId = request.UserId, RoleId = request.UserId == null ? request.RoleId : null };
            // تخصيص جديد لمستخدم يبدأ بنطاق دوره وحده ما لم يُرسل غيرهما
            var inherited = before.Settings;
            existing.Scope = inherited.Scope;
            existing.ScopeWarehouseIds = Csv(inherited.WarehouseIds);
            existing.ApprovalLimit = inherited.ApprovalLimit;
            _db.Add(existing);
        }
        // إعدادات فقط (نطاق/حد) بلا قائمة صلاحيات: تبقى الصلاحيات كما هي؛ ولمستخدم بلا تخصيص تُثبَّت صلاحيات دوره صراحةً
        // فيصبح النطاق تخصيصاً له دون أن تتغير صلاحياته
        var settingsOnly = regular.Count == 0 && platformGrant == null && request.Settings != null;
        if (settingsOnly)
        {
            if (request.UserId.HasValue && !PermissionStore.HasRegular(existing))
                foreach (var screen in PermissionCatalog.Screens)
                {
                    var granted = screen.Actions.Where(a => before.Allows(screen.Id, a)).ToHashSet();
                    var cols = PermissionStore.ColumnsOf(granted);
                    existing.Permissions.Add(new ScreenPermissionItem
                    {
                        ScreenId = screen.Id, CanView = cols[0], CanCreate = cols[1], CanEdit = cols[2], CanDelete = cols[3], CanApprove = cols[4],
                        Actions = PermissionStore.FormatActions(granted),
                    });
                }
        }
        else if (existing.Id != Guid.Empty || existing.Permissions.Count > 0)
        {
            var kept = existing.Permissions.FirstOrDefault(p => p.ScreenId == PlatformScreenId);
            if (kept != null) platform = (kept.CanView, kept.CanEdit);
            _db.RemoveRange(existing.Permissions);
            existing.Permissions.Clear();
        }
        if (platformGrant != null) platform = (platformGrant.CanView, platformGrant.CanEdit);

        foreach (var p in regular.GroupBy(p => p.ScreenId).Select(g => g.Last()))
        {
            var screen = PermissionCatalog.Find(p.ScreenId)!;
            if (p.Actions == null)
            {
                // عميل قديم: الأعمدة الخمسة وحدها، وتُشتق منها الإجراءات المستقلة
                existing.Permissions.Add(new ScreenPermissionItem
                {
                    ScreenId = p.ScreenId, CanView = p.CanView, CanCreate = p.CanCreate,
                    CanEdit = p.CanEdit, CanDelete = p.CanDelete, CanApprove = p.CanApprove,
                });
                continue;
            }
            var granted = PermissionStore.ParseActions(string.Join(",", p.Actions)).Where(screen.Actions.Contains).ToHashSet();
            var cols = PermissionStore.ColumnsOf(granted);
            existing.Permissions.Add(new ScreenPermissionItem
            {
                ScreenId = p.ScreenId, CanView = cols[0], CanCreate = cols[1], CanEdit = cols[2], CanDelete = cols[3], CanApprove = cols[4],
                Actions = PermissionStore.FormatActions(granted),
            });
        }
        if (platform is { } g && (g.View || g.Edit))
            existing.Permissions.Add(new ScreenPermissionItem { ScreenId = PlatformScreenId, CanView = g.View, CanEdit = g.Edit });

        if (request.Settings != null)
        {
            existing.Scope = request.Settings.Scope;
            existing.ScopeWarehouseIds = request.Settings.Scope == DataScope.Branches ? Csv(request.Settings.WarehouseIds) : null;
            existing.ApprovalLimit = request.Settings.ApprovalLimit;
        }

        var after = PermissionStore.Build(existing, roleCode);
        _db.AddRange(PermissionStore.Diff(before, after, (perm, old, now) =>
            Log(request.UserId.HasValue ? "user" : "role", request.UserId?.ToString() ?? roleCode, targetName, perm, old, now)));
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var subject = await _store.SubjectAsync(userId, ct) ?? throw new NotFoundException(Messages.UserNotFound);
        var row = await _store.UserRowAsync(userId, ct, track: true, includePlatformOnly: true);
        if (row == null || !PermissionStore.HasRegular(row)) return;

        var before = PermissionStore.Build(row, subject.RoleCode);
        var after = await _store.RoleGrantsAsync(subject.RoleCode, ct);
        // منحة المنصة ليست تخصيصاً للصلاحيات فتبقى
        var regular = row.Permissions.Where(p => p.ScreenId != PlatformScreenId).ToList();
        _db.RemoveRange(regular);
        if (regular.Count == row.Permissions.Count) _db.Remove(row);
        _db.AddRange(PermissionStore.Diff(before, after, (perm, old, now) => Log("user", userId.ToString(), subject.Name, perm, old, now)));
        _db.Add(Log("user", userId.ToString(), subject.Name, "reset", "direct", "role"));
        await _db.SaveChangesAsync(ct);
    }

    private PermissionChangeLog Log(string type, string id, string name, string permission, string? old, string? now) => new()
    {
        ChangedByUserId = _user.UserId, ChangedByName = _user.Name ?? "system",
        TargetType = type, TargetId = id, TargetName = name, Permission = permission, OldValue = old, NewValue = now,
        Timestamp = DateTime.UtcNow,
    };

    private static string? Csv(IEnumerable<Guid> ids)
    {
        var s = string.Join(",", ids.Distinct());
        return s.Length == 0 ? null : s;
    }

    // ---------- الفحص للمستخدم الحالي ----------

    private async Task<(PermissionSubject? Subject, PermissionGrants Grants)> MineAsync(CancellationToken ct)
    {
        if (_mine.HasValue) return _mine.Value;
        var subject = _user.UserId.HasValue ? await _store.SubjectAsync(_user.UserId.Value, ct) : null;
        var grants = subject == null ? new PermissionGrants() : await _store.GrantsForAsync(subject, ct);
        _mine = (subject, grants);
        return _mine.Value;
    }

    public Task<bool> HasPermissionAsync(string screenId, ScreenAction action, CancellationToken ct = default)
        => HasPermissionAsync(screenId, action, null, ct);

    public async Task<bool> HasPermissionAsync(string screenId, ScreenAction action, ScreenAction? legacy, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.RoleId == null) return false;
        if (screenId == PlatformScreenId) return false;
        var role = _user.RoleId;
        if (role == EmployeeRoleId) return false; // بوابة الموظف فقط: لا شاشة من شاشات النظام، مهما حُفظ للدور من صلاحيات
        if (PermissionCatalog.Find(screenId) == null) return false; // شاشة خارج الكتالوج = خطأ برمجي، لا تُفتح

        if (screenId == "user-permissions" && PermissionAdminRoles.Contains(role)) return true;
        if (PermissionStore.IsFullAccess(role)) return true;

        var (subject, grants) = await MineAsync(ct);
        if (subject is not { IsActive: true, RoleActive: true }) return false;
        return grants.Allows(screenId, action, legacy);
    }

    /// <summary>مرحلة لا تسمح بالإجراء = حالة المستند (409)؛ وما عداها (صلاحية، نطاق، حد) = 403. السبب في الرسالة.</summary>
    private static ErpException Denied(AuthorizationCheckResultDto result)
        => result.ReasonCode == "stage_disallows" ? new ConflictException(result.Reason!) : new ForbiddenException(result.Reason);

    public async Task AuthorizeRecordAsync(string screenId, ScreenAction action, string recordType, Guid recordId, ScreenAction? legacy = null, CancellationToken ct = default)
    {
        var type = ScopedRecordTypes.Find(recordType) ?? throw new ValidationFailedException(string.Format(Messages.UnknownRecordType, recordType));
        var facts = await LoadFactsAsync(type, recordId, ct) ?? throw new NotFoundException(Messages.PermissionCheckRecordNotFound);
        var (subject, grants) = await MineAsync(ct);
        var result = await EvaluateAsync(subject, grants, screenId, action, legacy, type, facts, null, ct);
        if (!result.Allowed) throw Denied(result);
    }

    public async Task AuthorizeAnyAsync(string recordType, Guid recordId, IReadOnlyCollection<ScreenAction> anyOf, CancellationToken ct = default)
    {
        var type = ScopedRecordTypes.Find(recordType) ?? throw new ValidationFailedException(string.Format(Messages.UnknownRecordType, recordType));
        var facts = await LoadFactsAsync(type, recordId, ct) ?? throw new NotFoundException(Messages.PermissionCheckRecordNotFound);
        var (subject, grants) = await MineAsync(ct);

        var failures = new List<AuthorizationCheckResultDto>();
        foreach (var action in anyOf)
        {
            // المرحلة لا تُفحص هنا: الانتقال المسموح من حالته يحدده جدول الانتقالات
            var result = await EvaluateAsync(subject, grants, type.ScreenId, action, null, type, facts, null, ct, skipStage: true);
            if (result.Allowed) return;
            failures.Add(result);
        }
        // سبب أدق من «لا صلاحية» (خارج النطاق، تجاوز الحد) يتقدّم؛ وإلا تُذكر الصلاحيات المطلوبة كلها
        var specific = failures.FirstOrDefault(f => f.ReasonCode != "no_permission");
        if (specific != null) throw Denied(specific);
        throw new ForbiddenException(string.Format(Messages.ContractTransitionNoPermission,
            string.Join(" / ", anyOf.Select(a => PermissionCatalog.Action(a).NameAr))));
    }

    public async Task EnsureRecordInScopeAsync(string recordType, Guid recordId, CancellationToken ct = default)
    {
        var type = ScopedRecordTypes.Find(recordType);
        if (type == null) return;
        var (subject, grants) = await MineAsync(ct);
        if (subject == null || grants.FullAccess || grants.Settings.Scope == DataScope.All) return;
        var facts = await LoadFactsAsync(type, recordId, ct);
        if (facts == null) return; // غير موجود: يُترك للعملية نفسها (404)
        var step = await ScopeStepAsync(subject, grants.Settings, facts, ct);
        if (step.Status == "failed") throw new ForbiddenException(string.Format(Messages.PermissionReasonOutOfScope, ScopeName(grants.Settings.Scope)));
    }

    public async Task EnsureApprovalLimitAsync(decimal amount, CancellationToken ct = default)
    {
        if (PermissionStore.IsFullAccess(_user.RoleId)) return;
        var (_, grants) = await MineAsync(ct);
        if (grants.FullAccess || grants.Settings.ApprovalLimit is not { } limit) return;
        if (amount > limit) throw new ForbiddenException(string.Format(Messages.PermissionReasonLimit, amount, limit));
    }

    public async Task<DataScopeFilter?> GetDataScopeAsync(string screenId, CancellationToken ct = default)
    {
        if (PermissionStore.IsFullAccess(_user.RoleId)) return null;
        var (subject, grants) = await MineAsync(ct);
        if (subject?.UserId == null || grants.FullAccess || grants.Settings.Scope == DataScope.All) return null;
        return new DataScopeFilter
        {
            UserId = subject.UserId.Value, Scope = grants.Settings.Scope, WarehouseIds = grants.Settings.WarehouseIds,
            DepartmentUserIds = grants.Settings.Scope == DataScope.Department ? await DepartmentUsersAsync(subject, ct) : Array.Empty<Guid>(),
        };
    }

    // ---------- التقييم الكامل: مستخدم ← صلاحية ← نطاق ← مرحلة ← حد اعتماد ----------

    private async Task<AuthorizationCheckResultDto> EvaluateAsync(PermissionSubject? subject, PermissionGrants grants, string screenId, A action,
        A? legacy, ScopedRecordType? type, RecordFacts? facts, decimal? amount, CancellationToken ct, bool skipStage = false)
    {
        var result = new AuthorizationCheckResultDto();
        var screen = PermissionCatalog.Find(screenId);
        var actionName = PermissionCatalog.Action(action).NameAr;
        AuthorizationCheckResultDto Deny(string code, string reason)
        {
            result.Allowed = false; result.ReasonCode = code; result.Reason = reason;
            return result;
        }
        void Step(string key, string status, string? detail) => result.Steps.Add(new AuthorizationStepDto { Key = key, Status = status, Detail = detail });

        // 1) المستخدم ودوره
        if (subject == null || !subject.IsActive)
        {
            Step("user", "failed", Messages.PermissionReasonUserInactive);
            return Deny("user_inactive", Messages.PermissionReasonUserInactive);
        }
        if (!subject.RoleActive)
        {
            var msg = string.Format(Messages.PermissionReasonRoleDisabled, subject.RoleNameAr);
            Step("user", "failed", msg);
            return Deny("role_disabled", msg);
        }
        Step("user", "passed", Messages.PermissionStepUserActive);

        // 2) الصلاحية
        var full = grants.FullAccess || subject.FullAccess;
        var allowed = full || (screen != null && subject.RoleCode != EmployeeRoleId
            && ((screenId == "user-permissions" && PermissionAdminRoles.Contains(subject.RoleCode)) || grants.Allows(screenId, action, legacy)));
        if (!allowed)
        {
            var msg = string.Format(Messages.PermissionReasonNoPermission, actionName, screen?.NameAr ?? screenId);
            Step("permission", "failed", msg);
            return Deny("no_permission", msg);
        }
        Step("permission", "passed", full ? Messages.PermissionStepFullAccess : string.Format(Messages.PermissionStepGranted, SourceName(grants.Source)));

        // 3) النطاق
        if (facts == null || full) Step("scope", "skipped", Messages.PermissionStepNotApplicable);
        else
        {
            var scope = await ScopeStepAsync(subject, grants.Settings, facts, ct);
            result.Steps.Add(scope);
            if (scope.Status == "failed") return Deny("out_of_scope", scope.Detail!);
        }

        // 4) المرحلة (القراءة لا تتقيّد بها)
        var stage = !skipStage && type?.HasStages == true && !ReadActions.Contains(action) ? await StageAsync(facts?.Stage, ct) : null;
        if (stage == null) Step("stage", "skipped", Messages.PermissionStepNotApplicable);
        else if (!PermissionStore.ParseActions(stage.AllowedActions).Contains(action))
        {
            var msg = string.Format(Messages.PermissionReasonStage, stage.NameAr, actionName);
            Step("stage", "failed", msg);
            return Deny("stage_disallows", msg);
        }
        else Step("stage", "passed", string.Format(Messages.PermissionStepStageOk, stage.NameAr));

        // 5) حد الاعتماد (للاعتماد فقط)
        var value = amount ?? facts?.Amount;
        if (action != A.Approve || full || value == null) Step("limit", "skipped", Messages.PermissionStepNotApplicable);
        else if (grants.Settings.ApprovalLimit is not { } limit) Step("limit", "passed", Messages.PermissionStepNoLimit);
        else if (value > limit)
        {
            var msg = string.Format(Messages.PermissionReasonLimit, value, limit);
            Step("limit", "failed", msg);
            return Deny("exceeds_limit", msg);
        }
        else Step("limit", "passed", string.Format(Messages.PermissionStepLimitOk, value, limit));

        result.Allowed = true;
        return result;
    }

    private async Task<AuthorizationStepDto> ScopeStepAsync(PermissionSubject subject, PermissionSettingsDto settings, RecordFacts facts, CancellationToken ct)
    {
        var me = subject.UserId;
        var inScope = settings.Scope switch
        {
            DataScope.All => true,
            DataScope.Branches => facts.WarehouseId == null || settings.WarehouseIds.Contains(facts.WarehouseId.Value),
            DataScope.Own => facts.CreatedByUserId == me,
            DataScope.Assigned => facts.AssignedToUserId == me || facts.CreatedByUserId == me,
            DataScope.Department => facts.CreatedByUserId is { } c && (c == me || (await DepartmentUsersAsync(subject, ct)).Contains(c)),
            _ => false,
        };
        var name = ScopeName(settings.Scope);
        return new AuthorizationStepDto
        {
            Key = "scope", Status = inScope ? "passed" : "failed",
            Detail = string.Format(inScope ? Messages.PermissionStepScopeOk : Messages.PermissionReasonOutOfScope, name),
        };
    }

    private async Task<IReadOnlyCollection<Guid>> DepartmentUsersAsync(PermissionSubject subject, CancellationToken ct)
    {
        if (subject.Department == null) return Array.Empty<Guid>();
        return await _db.Set<User>().AsNoTracking().Where(u => u.Department != null && u.Department.Trim() == subject.Department)
            .Select(u => u.Id).ToListAsync(ct);
    }

    private async Task<ContractStageDefinition?> StageAsync(string? stageId, CancellationToken ct)
        => stageId == null ? null : await _db.Set<ContractStageDefinition>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stageId, ct);

    private async Task<RecordFacts?> LoadFactsAsync(ScopedRecordType type, Guid id, CancellationToken ct)
    {
        using (_db.SuspendDataScope())
            return await ScopedRecordTypes.LoadAsync(_db, type, id, ct);
    }

    private static string ScopeName(DataScope scope) => scope switch
    {
        DataScope.Branches => Messages.ScopeBranches,
        DataScope.Department => Messages.ScopeDepartment,
        DataScope.Own => Messages.ScopeOwn,
        DataScope.Assigned => Messages.ScopeAssigned,
        _ => Messages.ScopeAll,
    };

    private static string SourceName(string source) => source switch
    {
        "user" => Messages.PermissionSourceDirect,
        "role" => Messages.PermissionSourceRole,
        _ => Messages.PermissionSourceDefault,
    };

    // ---------- مركز الصلاحيات ----------

    public async Task<EffectivePermissionsDto> GetEffectiveAsync(Guid userId, CancellationToken ct = default)
    {
        var subject = await _store.SubjectAsync(userId, ct) ?? throw new NotFoundException(Messages.UserNotFound);
        var role = await _store.RoleGrantsAsync(subject.RoleCode, ct);
        var userRow = subject.FullAccess ? null : await _store.UserRowAsync(userId, ct);
        var direct = userRow == null ? null : PermissionStore.Build(userRow, subject.RoleCode, "user");
        var effective = direct ?? role;
        var blocked = !subject.IsActive || !subject.RoleActive || subject.RoleCode == EmployeeRoleId;

        var dto = new EffectivePermissionsDto
        {
            UserId = userId, UserName = subject.Name, IsActive = subject.IsActive, RoleCode = subject.RoleCode,
            RoleNameAr = subject.RoleNameAr, RoleNameEn = subject.RoleNameEn, RoleActive = subject.RoleActive,
            FullAccess = subject.FullAccess, HasDirect = direct != null, Settings = effective.Settings,
            SettingsSource = direct != null ? "direct" : role.Source == "role" ? "role" : "default",
        };
        foreach (var screen in PermissionCatalog.Screens)
        {
            var row = new EffectiveScreenDto { ScreenId = screen.Id };
            foreach (var action in screen.Actions)
            {
                var allowed = !blocked && (subject.FullAccess || effective.Allows(screen.Id, action));
                var source = !allowed ? "none"
                    : subject.FullAccess ? "full"
                    : direct != null ? (role.Allows(screen.Id, action) ? "role" : "direct")
                    : role.Source == "role" ? "role" : "default";
                row.Actions.Add(new EffectiveActionDto { Action = PermissionCatalog.Code(action), Allowed = allowed, Source = source });
            }
            dto.Screens.Add(row);
        }
        var all = dto.Screens.SelectMany(s => s.Actions).ToList();
        dto.GrantedCount = all.Count(a => a.Allowed);
        dto.DirectCount = all.Count(a => a.Source == "direct");
        return dto;
    }

    public async Task<List<Guid>> GetCustomizedUsersAsync(CancellationToken ct = default)
        => await _db.Set<UserRolePermission>().AsNoTracking()
            .Where(p => p.UserId != null && p.Permissions.Any(i => i.ScreenId != PlatformScreenId))
            .Select(p => p.UserId!.Value).ToListAsync(ct);

    public async Task<AuthorizationCheckResultDto> CheckAsync(AuthorizationCheckRequestDto request, CancellationToken ct = default)
    {
        if (!PermissionCatalog.TryParse(request.Action, out var action))
            throw new ValidationFailedException(string.Format(Messages.UnknownPermissionAction, request.Action));
        if (PermissionCatalog.Find(request.ScreenId) == null)
            throw new ValidationFailedException(Messages.UnknownScreensPrefix + request.ScreenId);

        var subject = await _store.SubjectAsync(request.UserId, ct) ?? throw new NotFoundException(Messages.UserNotFound);
        var grants = await _store.GrantsForAsync(subject, ct);

        ScopedRecordType? type = null;
        RecordFacts? facts = null;
        if (!string.IsNullOrWhiteSpace(request.RecordType) && request.RecordId.HasValue)
        {
            type = ScopedRecordTypes.Find(request.RecordType) ?? throw new ValidationFailedException(string.Format(Messages.UnknownRecordType, request.RecordType));
            facts = await LoadFactsAsync(type, request.RecordId.Value, ct);
            if (facts == null)
                return new AuthorizationCheckResultDto { Allowed = false, ReasonCode = "record_not_found", Reason = Messages.PermissionCheckRecordNotFound };
        }

        var result = await EvaluateAsync(subject, grants, request.ScreenId, action, null, type, facts, request.Amount, ct);
        if (facts != null) result.Record = ToRecordDto(type!, facts);
        return result;
    }

    public async Task<List<AuthorizationRecordDto>> SearchRecordsAsync(string recordType, string? term, CancellationToken ct = default)
    {
        var type = ScopedRecordTypes.Find(recordType) ?? throw new ValidationFailedException(string.Format(Messages.UnknownRecordType, recordType));
        using (_db.SuspendDataScope())
            return (await ScopedRecordTypes.SearchAsync(_db, type, term, ct)).Select(f => ToRecordDto(type, f)).ToList();
    }

    private static AuthorizationRecordDto ToRecordDto(ScopedRecordType type, RecordFacts f) => new()
    {
        Id = f.Id, RecordType = type.Key, Number = f.Number, Amount = f.Amount, Stage = f.Stage,
        WarehouseId = f.WarehouseId, CreatedByUserId = f.CreatedByUserId, AssignedToUserId = f.AssignedToUserId,
    };

    public async Task AssignRecordAsync(AssignRecordDto request, CancellationToken ct = default)
    {
        var type = ScopedRecordTypes.Find(request.RecordType) ?? throw new ValidationFailedException(string.Format(Messages.UnknownRecordType, request.RecordType));
        if (request.UserId.HasValue && !await _db.Set<User>().AnyAsync(u => u.Id == request.UserId, ct))
            throw new NotFoundException(Messages.UserNotFound);
        using (_db.SuspendDataScope())
        {
            var entity = await _db.FindAsync(type.EntityType, request.RecordId) as IScopedRecord
                ?? throw new NotFoundException(Messages.PermissionCheckRecordNotFound);
            if (((BaseEntity)entity).TenantId != _db.CurrentTenantId) throw new NotFoundException(Messages.PermissionCheckRecordNotFound);
            entity.AssignedToUserId = request.UserId;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<PagedResult<PermissionChangeLogDto>> GetChangeLogAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<PermissionChangeLog>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var term = p.SearchTerm.Trim();
            q = q.Where(l => l.TargetName.Contains(term) || l.Permission.Contains(term) || l.ChangedByName.Contains(term));
        }
        if (p.StartDate.HasValue) q = q.Where(l => l.Timestamp >= p.StartDate.Value);
        if (p.EndDate.HasValue) q = q.Where(l => l.Timestamp < p.EndDate.Value.Date.AddDays(1));

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(l => l.Timestamp).ThenByDescending(l => l.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize)
            .Select(l => new PermissionChangeLogDto
            {
                Id = l.Id, ChangedByUserId = l.ChangedByUserId, ChangedByName = l.ChangedByName, TargetType = l.TargetType,
                TargetId = l.TargetId, TargetName = l.TargetName, Permission = l.Permission, OldValue = l.OldValue,
                NewValue = l.NewValue, Timestamp = l.Timestamp,
            }).ToListAsync(ct);
        return new PagedResult<PermissionChangeLogDto> { Items = items, TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize };
    }
}
