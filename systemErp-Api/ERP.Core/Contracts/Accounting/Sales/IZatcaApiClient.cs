namespace ERP.Core.Contracts.Accounting;

/// <summary>شهادة أصدرتها الهيئة (امتثال أو إنتاج): رمز الأمان (الشهادة مرمَّزة) والسر، وبهما تُصادَق الطلبات التالية.</summary>
public sealed record ZatcaCsidResult(bool Success, string? RequestId, string? BinarySecurityToken, string? Secret, string? Message);

/// <summary>فاتورة موقّعة جاهزة للإرسال.</summary>
public sealed record ZatcaSubmission(string InvoiceHash, Guid Uuid, string Xml);

/// <summary>
/// رد الهيئة على فاتورة. <see cref="Reached"/> = وصل الطلب وعاد حكم (قبول أو رفض)؛ false = تعذّر الاتصال أو خطأ
/// في الخادم، فلا حكم على الفاتورة ويُعاد الإرسال لاحقاً.
/// </summary>
public sealed class ZatcaApiResult
{
    public bool Reached { get; init; }
    public int HttpStatus { get; init; }
    /// <summary>قُبلت الفاتورة (مع تحذيرات أو بدونها).</summary>
    public bool Accepted { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    /// <summary>الفاتورة الضريبية بعد اعتمادها: وثيقة ختمتها الهيئة (Base64) وهي النسخة التي تُسلَّم للمشتري.</summary>
    public string? ClearedInvoice { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// واجهة الفوترة الإلكترونية لدى هيئة الزكاة (فاتورة): إصدار شهادتي الامتثال والإنتاج، فحص الامتثال، التبليغ عن
/// الفاتورة المبسطة، واعتماد الفاتورة الضريبية. <c>baseUrl</c> يحدد البيئة (بوابة المطورين، المحاكاة، الإنتاج).
/// </summary>
public interface IZatcaApiClient
{
    Task<ZatcaCsidResult> RequestComplianceCsidAsync(string baseUrl, string csrBase64, string otp, CancellationToken ct = default);
    Task<ZatcaCsidResult> RequestProductionCsidAsync(string baseUrl, string complianceRequestId, string token, string secret, CancellationToken ct = default);
    Task<ZatcaApiResult> CheckComplianceAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default);
    Task<ZatcaApiResult> ReportAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default);
    Task<ZatcaApiResult> ClearAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default);
}
