namespace ERP.Core.Models.Shared;

/// <summary>
/// نص رسالة الفاتورة كما كتبه المستخدم (قاعدة المنشأة وحدها). كل حفظ قالب جديد لدى Meta باسم فريد، فيبقى القالب المعتمد السابق
/// مستعملاً حتى يُعتمد الجديد: الإرسال يأخذ أحدث قالب معتمد للغة، وإلا قالب الفاتورة الافتراضي.
/// المتغيرات تُحفظ بأسمائها ({customer_name}) وتُحوَّل لمتغيرات Meta الموضعية ({{1}}..) بترتيب ظهورها في <see cref="VariableKeys"/>.
/// </summary>
public class WhatsAppInvoiceTemplate : BaseEntity
{
    /// <summary>اسم القالب لدى Meta (erp_invoice_...).</summary>
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "ar";
    /// <summary>النص بمتغيرات مسمّاة كما كتبه المستخدم.</summary>
    public string Body { get; set; } = string.Empty;
    public string? Footer { get; set; }
    /// <summary>مفاتيح المتغيرات بترتيب ظهورها في النص (مفصولة بفواصل).</summary>
    public string VariableKeys { get; set; } = string.Empty;
    /// <summary>حالة Meta: PENDING | APPROVED | REJECTED | PAUSED | DISABLED.</summary>
    public string Status { get; set; } = "PENDING";
    public string? Reason { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
}
