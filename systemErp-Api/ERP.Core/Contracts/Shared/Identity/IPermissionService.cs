using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IPermissionService
{
    List<ScreenDefinitionDto> GetScreens();
    /// <summary>الوحدات والشاشات والإجراءات وأنواع السجلات (بلا نقاط الـ API؛ تضيفها طبقة الـ API من سماتها).</summary>
    PermissionCatalogDto GetCatalog();
    /// <summary>صلاحيات مستخدم محدد، ثم دوره، ثم الافتراضي (كما في PermissionService بالواجهة).</summary>
    Task<List<ScreenPermissionDto>> GetForUserOrRoleAsync(Guid? userId, string? roleId, CancellationToken ct = default);
    /// <summary>
    /// شبكة تعديل الصلاحيات: الصلاحيات نفسها، ويُضاف لها سطر إدارة المنصة عند تعديل مستخدم محدد من قِبل مدير منصة فقط.
    /// </summary>
    Task<List<ScreenPermissionDto>> GetForEditingAsync(Guid? userId, string? roleId, CancellationToken ct = default);
    /// <summary>نطاق الوصول وحد الاعتماد المحفوظان لدور أو مستخدم (المستخدم بلا تخصيص يرث دوره).</summary>
    Task<PermissionSettingsDto> GetSettingsAsync(Guid? userId, string? roleId, CancellationToken ct = default);
    Task SaveAsync(SavePermissionsRequestDto request, CancellationToken ct = default);
    /// <summary>حذف التخصيص المباشر لمستخدم فيعود لصلاحيات دوره.</summary>
    Task ResetUserAsync(Guid userId, CancellationToken ct = default);

    Task<bool> HasPermissionAsync(string screenId, ScreenAction action, CancellationToken ct = default);
    /// <summary>
    /// كالسابق، و<paramref name="legacy"/> عمود الصلاحية الذي كان يحمي النقطة قبل الإجراءات المستقلة: يُفحص بدل الإجراء
    /// ما دامت صلاحيات الشاشة محفوظة بالأعمدة الخمسة فقط (لم تُحفظ من المحرر الجديد)، فلا يتغير سلوك البيانات القائمة.
    /// </summary>
    Task<bool> HasPermissionAsync(string screenId, ScreenAction action, ScreenAction? legacy, CancellationToken ct = default);

    /// <summary>صلاحية + نطاق + مرحلة + حد اعتماد على سجل محدد للمستخدم الحالي؛ يرمي 403 بسبب الرفض.</summary>
    Task AuthorizeRecordAsync(string screenId, ScreenAction action, string recordType, Guid recordId, ScreenAction? legacy = null, CancellationToken ct = default);
    /// <summary>أي إجراء من <paramref name="anyOf"/> على سجل محدد: صلاحية + نطاق + حد اعتماد (بلا فحص المرحلة: الانتقال يحدده جدول الانتقالات)؛ يرمي 403 بسبب الرفض.</summary>
    Task AuthorizeAnyAsync(string recordType, Guid recordId, IReadOnlyCollection<ScreenAction> anyOf, CancellationToken ct = default);
    /// <summary>السجل ضمن نطاق المستخدم الحالي وإلا 403 (السجل غير الموجود يُترك للعملية نفسها).</summary>
    Task EnsureRecordInScopeAsync(string recordType, Guid recordId, CancellationToken ct = default);
    /// <summary>المبلغ ضمن حد اعتماد المستخدم الحالي وإلا 403 (لعمليات اعتماد بلا نوع سجل ذي نطاق).</summary>
    Task EnsureApprovalLimitAsync(decimal amount, CancellationToken ct = default);
    /// <summary>نطاق قوائم المستخدم الحالي لشاشة؛ null = كل السجلات.</summary>
    Task<DataScopeFilter?> GetDataScopeAsync(string screenId, CancellationToken ct = default);

    /// <summary>المستخدمون النشطون الذين يملكون أياً من الإجراءات على الشاشة (لتوجيه الإشعارات).</summary>
    Task<List<Guid>> UsersWithAnyActionAsync(string screenId, IReadOnlyCollection<ScreenAction> anyOf, CancellationToken ct = default);
    Task<EffectivePermissionsDto> GetEffectiveAsync(Guid userId, CancellationToken ct = default);
    /// <summary>المستخدمون الذين لهم تخصيص مباشر يتفوق على صلاحيات أدوارهم.</summary>
    Task<List<Guid>> GetCustomizedUsersAsync(CancellationToken ct = default);
    Task<AuthorizationCheckResultDto> CheckAsync(AuthorizationCheckRequestDto request, CancellationToken ct = default);
    Task<List<AuthorizationRecordDto>> SearchRecordsAsync(string recordType, string? term, CancellationToken ct = default);
    Task AssignRecordAsync(AssignRecordDto request, CancellationToken ct = default);
    Task<PagedResult<PermissionChangeLogDto>> GetChangeLogAsync(PaginationParams p, CancellationToken ct = default);
}

/// <summary>مرشّح نطاق القوائم: يطبَّق على المستندات ذات النطاق في استعلامات القوائم فقط.</summary>
public sealed class DataScopeFilter
{
    public Guid UserId { get; init; }
    public DataScope Scope { get; init; }
    public IReadOnlyCollection<Guid> WarehouseIds { get; init; } = Array.Empty<Guid>();
    /// <summary>مستخدمو قسم المستخدم (نطاق القسم).</summary>
    public IReadOnlyCollection<Guid> DepartmentUserIds { get; init; } = Array.Empty<Guid>();
}
