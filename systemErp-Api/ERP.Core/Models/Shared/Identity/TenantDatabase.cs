namespace ERP.Core.Models.Shared;

/// <summary>قاعدة بيانات المنشأة: المشتركة (الافتراضي، عزل بـ TenantId) أو مستقلة تُجهَّز لها وحدها.</summary>
public enum TenantDatabaseMode
{
    Shared = 1,
    Dedicated = 2,
}

public enum TenantDatabaseStatus
{
    /// <summary>أُنشئت المنشأة ولم تكتمل قاعدتها بعد: لا يُسمح بالعمل عليها حتى تجهز (لا رجوع صامت للمشتركة).</summary>
    Provisioning = 1,
    Ready = 2,
    Failed = 3,
}

/// <summary>
/// ربط منشأة بقاعدتها المستقلة، في القاعدة المشتركة (الدليل) فقط وعلى مستوى المنصة (لا يتبع منشأة ولا يُعدَّل إلا من مدير المنصة).
/// المنشأة بلا سجل هنا تعمل على القاعدة المشتركة. نص الاتصال مشفَّر (Data Protection) ولا يخرج في أي استجابة.
/// </summary>
public class TenantDatabase
{
    public Guid TenantId { get; set; }
    public TenantDatabaseMode Mode { get; set; } = TenantDatabaseMode.Dedicated;
    public TenantDatabaseStatus Status { get; set; } = TenantDatabaseStatus.Provisioning;
    public string DatabaseName { get; set; } = string.Empty;
    public string ConnectionStringEncrypted { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProvisionedAt { get; set; }
}
