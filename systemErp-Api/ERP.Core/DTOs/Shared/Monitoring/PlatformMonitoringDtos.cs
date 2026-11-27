namespace ERP.Core.DTOs.Shared;

// =====================================================================
// مراقبة المنصة (مدير المنصة فقط): المنشآت وقواعدها، أخطاء الخادم، الدخول، الوحدات. لا بيانات أعمال للمنشآت.
// =====================================================================

public class PlatformMonitoringOverviewDto
{
    public DateTime GeneratedAt { get; set; }
    /// <summary>حالات الاشتراك (من ملخص المنصة نفسه).</summary>
    public PlatformSummaryDto Subscriptions { get; set; } = new();
    public int SharedDatabaseTenants { get; set; }
    public int DedicatedReady { get; set; }
    public int DedicatedProvisioning { get; set; }
    public int DedicatedFailed { get; set; }
    /// <summary>اتصال القاعدة المشتركة وزمنه الفعلي عند الطلب.</summary>
    public bool CatalogReachable { get; set; }
    public int? CatalogLatencyMs { get; set; }
    public int ApiErrors24h { get; set; }
    public int ApiErrors7d { get; set; }
    public int SuccessfulLogins24h { get; set; }
    public int FailedLogins24h { get; set; }
    /// <summary>عدد المنشآت المفعّلة لكل وحدة (من آخر اشتراك لكل منشأة).</summary>
    public Dictionary<string, int> ActiveModules { get; set; } = new();
    public PlatformCheckRunDto? LastCheck { get; set; }
}

public class PlatformApiErrorRowDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Message { get; set; }
    public DateTime OccurredAt { get; set; }
}

/// <summary>صحة قاعدة منشأة مستقلة في آخر فحص (لا نص اتصال ولا أسماء خوادم).</summary>
public class PlatformDatabaseHealthDto
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    /// <summary>ready | provisioning | failed</summary>
    public string Status { get; set; } = string.Empty;
    public bool? Reachable { get; set; }
    public int? LatencyMs { get; set; }
    /// <summary>سبب فشل التجهيز المحفوظ (مقصوص).</summary>
    public string? LastError { get; set; }
}

/// <summary>تنبيه منصة محسوب في الفحص (لقطة، يُعاد حسابه في كل فحص).</summary>
public class PlatformAlertDto
{
    public string RuleKey { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public Dictionary<string, string> Params { get; set; } = new();
}

public class PlatformCheckRunDto
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RunByName { get; set; } = string.Empty;
    public string? Error { get; set; }
    public List<PlatformDatabaseHealthDto> Databases { get; set; } = new();
    public List<PlatformAlertDto> Alerts { get; set; } = new();
}
