namespace ERP.Core.Models.Shared;

/// <summary>
/// خطأ خادم غير متوقع (500) بمعرّف تتبعه الذي يُعاد للمستخدم. على مستوى المنصة في القاعدة المشتركة وحدها (بلا مرشّح منشأة؛
/// القراءة تحدد `TenantId` صراحةً). معلومات آمنة فقط: المسار بلا معاملات الاستعلام، نوع الاستثناء وموضعه، ورسالة مقصوصة
/// منقّاة من أنماط الأسرار. لا جسم الطلب ولا الترويسات ولا تتبع المكدّس.
/// </summary>
public class ApiErrorLog
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ExceptionType { get; set; } = string.Empty;
    /// <summary>الصنف والدالة التي رمت الاستثناء.</summary>
    public string? Source { get; set; }
    public string? Message { get; set; }
    public DateTime OccurredAt { get; set; }
}
