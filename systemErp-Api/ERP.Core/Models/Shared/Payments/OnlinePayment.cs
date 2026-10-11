namespace ERP.Core.Models.Shared;

public enum OnlinePaymentPurpose
{
    Subscription = 1,   // اشتراك المنشأة (يُدفع لحساب المنصة)
    InvoicePayment = 2, // رابط دفع لفاتورة عميل آجلة (يُدفع لحساب المنشأة)
    PosSale = 3         // دفع إلكتروني في نقطة البيع (يُدفع لحساب المنشأة)
}

public enum OnlinePaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// محاولة دفع إلكتروني عبر Paymob. تُنشأ بحالة Pending مع رابط الدفع، ولا تُعدّ مدفوعة إلا بإشعار Paymob الموقَّع (HMAC).
/// الأثر (سند قبض / تفعيل باقة / إتمام بيع) يُنفَّذ مرة واحدة فقط عند أول إشعار نجاح.
/// </summary>
public class OnlinePayment : BaseEntity
{
    public string Provider { get; set; } = "paymob";
    public OnlinePaymentPurpose Purpose { get; set; }
    public OnlinePaymentStatus Status { get; set; } = OnlinePaymentStatus.Pending;

    /// <summary>مرجع فريد يُرسل لـ Paymob ويعود في الإشعار.</summary>
    public string SpecialReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Description { get; set; } = string.Empty;

    /// <summary>الفاتورة (InvoicePayment) أو الاشتراك الناتج (Subscription) أو عملية POS (PosSale بعد الإتمام).</summary>
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    /// <summary>للاشتراك: الباقة والدورة المطلوبتان (تُفعَّلان عند الدفع).</summary>
    public SubscriptionPlanId? PlanId { get; set; }
    public SubscriptionBillingCycle? BillingCycle { get; set; }

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public string? ProviderIntentionId { get; set; }
    public long? ProviderOrderId { get; set; }
    public long? ProviderTransactionId { get; set; }
    public string? ClientSecret { get; set; }
    public string CheckoutUrl { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>سند القبض المُنشأ آلياً (رابط دفع فاتورة).</summary>
    public Guid? VoucherId { get; set; }
    /// <summary>استُهلك الدفع في عملية بيع POS (لا يُستخدم مرتين).</summary>
    public DateTime? ConsumedAt { get; set; }
}
