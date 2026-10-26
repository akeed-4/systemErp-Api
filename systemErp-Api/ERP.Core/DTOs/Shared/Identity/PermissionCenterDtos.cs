namespace ERP.Core.DTOs.Shared;

/// <summary>نطاق الوصول وحد الاعتماد لدور أو مستخدم.</summary>
public class PermissionSettingsDto
{
    public DataScope Scope { get; set; } = DataScope.All;
    /// <summary>المستودعات (الفروع) عند نطاق «فروع محددة».</summary>
    public List<Guid> WarehouseIds { get; set; } = new();
    /// <summary>null = بلا حد.</summary>
    public decimal? ApprovalLimit { get; set; }
}

/// <summary>كتالوج الصلاحيات كما تعرضه الواجهة (المحرر، حارس المسارات، شاشة الكتالوج).</summary>
public class PermissionCatalogDto
{
    public List<PermissionActionDto> Actions { get; set; } = new();
    public List<PermissionModuleDto> Modules { get; set; } = new();
    public List<PermissionRecordTypeDto> RecordTypes { get; set; } = new();
    public int TotalPermissions { get; set; }
}

public class PermissionActionDto
{
    public string Code { get; set; } = string.Empty;
    /// <summary>access | data | workflow | reports</summary>
    public string Group { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
}

public class PermissionModuleDto
{
    public string Id { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<PermissionScreenDto> Screens { get; set; } = new();
}

public class PermissionScreenDto
{
    public string Id { get; set; } = string.Empty;
    public string ModuleId { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool Restricted { get; set; }
    public List<string> Actions { get; set; } = new();
    /// <summary>مسارات الواجهة التي تفتحها الشاشة (الأطول تطابقاً يتقدّم).</summary>
    public List<string> Routes { get; set; } = new();
    /// <summary>نقاط الـ API المحمية بكل إجراء، مستخرجة من سمات RequireScreen الفعلية.</summary>
    public List<PermissionEndpointDto> Endpoints { get; set; } = new();
}

public class PermissionEndpointDto
{
    public string Action { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
}

/// <summary>نوع سجل يخضع للنطاق والمرحلة وحد الاعتماد (لشاشة فحص الصلاحية).</summary>
public class PermissionRecordTypeDto
{
    public string Key { get; set; } = string.Empty;
    public string ScreenId { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public bool HasStages { get; set; }
}

/// <summary>دور في شاشة الأدوار: الأساسي والمخصّص، مع عدد مستخدميه وصلاحياته ونطاقه وحده.</summary>
public class RoleDto
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string? Description { get; set; }
    public UserRole BaseRole { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>المالك والمدير: كل الصلاحيات دائماً ولا تُعدَّل.</summary>
    public bool FullAccess { get; set; }
    public int UsersCount { get; set; }
    public int PermissionsCount { get; set; }
    public int TotalPermissions { get; set; }
    public PermissionSettingsDto Settings { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class SaveRoleDto
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Description { get; set; }
}

/// <summary>ما يستطيعه المستخدم فعلياً، ولكل إجراء مصدره (الدور أو صلاحية مباشرة).</summary>
public class EffectivePermissionsDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleNameAr { get; set; } = string.Empty;
    public string RoleNameEn { get; set; } = string.Empty;
    public bool RoleActive { get; set; } = true;
    /// <summary>مالك/مدير: كل الصلاحيات بلا قيود.</summary>
    public bool FullAccess { get; set; }
    /// <summary>للمستخدم تخصيص مباشر يتفوق على صلاحيات دوره.</summary>
    public bool HasDirect { get; set; }
    public PermissionSettingsDto Settings { get; set; } = new();
    /// <summary>role | direct | default</summary>
    public string SettingsSource { get; set; } = "role";
    public int GrantedCount { get; set; }
    public int DirectCount { get; set; }
    public List<EffectiveScreenDto> Screens { get; set; } = new();
}

public class EffectiveScreenDto
{
    public string ScreenId { get; set; } = string.Empty;
    public List<EffectiveActionDto> Actions { get; set; } = new();
}

public class EffectiveActionDto
{
    public string Action { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    /// <summary>role | direct | default | full | none</summary>
    public string Source { get; set; } = "none";
}

/// <summary>فحص صلاحية: المستخدم + العملية + السجل (اختياري) + مبلغ (اختياري، يتقدّم على مبلغ السجل).</summary>
public class AuthorizationCheckRequestDto
{
    public Guid UserId { get; set; }
    public string ScreenId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? RecordType { get; set; }
    public Guid? RecordId { get; set; }
    public decimal? Amount { get; set; }
}

public class AuthorizationCheckResultDto
{
    public bool Allowed { get; set; }
    /// <summary>no_permission | out_of_scope | stage_disallows | exceeds_limit | role_disabled | user_inactive | record_not_found</summary>
    public string? ReasonCode { get; set; }
    public string? Reason { get; set; }
    public List<AuthorizationStepDto> Steps { get; set; } = new();
    public AuthorizationRecordDto? Record { get; set; }
}

public class AuthorizationStepDto
{
    /// <summary>user | permission | scope | stage | limit</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>passed | failed | skipped</summary>
    public string Status { get; set; } = "skipped";
    public string? Detail { get; set; }
}

public class AuthorizationRecordDto
{
    public Guid Id { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string? Stage { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

public class PermissionChangeLogDto
{
    public Guid Id { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>إسناد سجل لمستخدم (نطاق «السجلات المسندة إليه»).</summary>
public class AssignRecordDto
{
    public string RecordType { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public Guid? UserId { get; set; }
}
