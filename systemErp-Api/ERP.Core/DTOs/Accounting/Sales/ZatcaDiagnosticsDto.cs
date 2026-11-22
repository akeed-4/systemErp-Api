namespace ERP.Core.DTOs.Accounting.Sales;

/// <summary>تقرير فحص اتصال الخادم ببوابة الهيئة خطوةً خطوة. لا يحوي رمز تحقق ولا بيانات اعتماد ولا مفاتيح.</summary>
public class ZatcaConnectivityReportDto
{
    public ZatcaEnvironment Environment { get; set; }
    public string Host { get; set; } = string.Empty;
    public string EndpointUrl { get; set; } = string.Empty;
    /// <summary>true = وصل رد HTTP من الهيئة عبر عميل HTTP نفسه الذي يُستخدم في الربط.</summary>
    public bool Reachable { get; set; }
    public int HttpClientTimeoutSeconds { get; set; }
    /// <summary>هل يوجد إعداد وسيط (Proxy) في متغيرات البيئة؛ لا تُعرض قيمته لأنها قد تحمل بيانات اعتماد.</summary>
    public bool ProxyConfigured { get; set; }
    public DateTime CheckedAt { get; set; }
    public List<ZatcaConnectivityStepDto> Steps { get; set; } = new();
}

public class ZatcaConnectivityStepDto
{
    /// <summary>dns | tcp | tls | https.</summary>
    public string Name { get; set; } = string.Empty;
    public bool Success { get; set; }
    public long ElapsedMs { get; set; }
    public int? StatusCode { get; set; }
    public string? Detail { get; set; }
    public string? ExceptionType { get; set; }
    /// <summary>رسائل الاستثناء وأسبابه الداخلية بالترتيب (بلا Stack Trace).</summary>
    public List<string> ExceptionChain { get; set; } = new();
}

/// <summary>
/// معالجة جلسة ربط عالقة بنتيجة مجهولة. <see cref="ConfirmNoCsidIssued"/> إقرار المسؤول بأنه تحقق في بوابة «فاتورة»
/// من عدم إصدار شهادة لهذا الطلب (مطلوب في المحاكاة والإنتاج؛ بوابة المطورين لا تسجل الشهادات على المكلف).
/// </summary>
public class ZatcaSessionRecoveryRequestDto
{
    public bool ConfirmNoCsidIssued { get; set; }
    public string? Note { get; set; }
}

/// <summary>جلسة ربط عالقة تحتاج قرار المسؤول.</summary>
public class ZatcaStuckSessionDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public ZatcaEnvironment Environment { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    /// <summary>هل يحفظ النظام أي شهادة أو رقم طلب من الهيئة لهذه الجلسة.</summary>
    public bool HasStoredCsid { get; set; }
    public bool CanRecover { get; set; }
    public bool RequiresPortalConfirmation { get; set; }
    public string? RecoveryBlockedReason { get; set; }
}
