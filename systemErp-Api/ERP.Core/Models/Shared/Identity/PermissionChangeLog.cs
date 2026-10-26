namespace ERP.Core.Models.Shared;

/// <summary>سجل تغيير صلاحية واحد: من غيّر، على أي دور/مستخدم، أي صلاحية، القيمة قبل وبعد.</summary>
public class PermissionChangeLog : BaseEntity
{
    public Guid? ChangedByUserId { get; set; }
    public string ChangedByName { get; set; } = string.Empty;
    /// <summary>role | user</summary>
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    /// <summary>رمز الصلاحية (screen.action) أو إعداد (scope / approval_limit / role.status ...).</summary>
    public string Permission { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
