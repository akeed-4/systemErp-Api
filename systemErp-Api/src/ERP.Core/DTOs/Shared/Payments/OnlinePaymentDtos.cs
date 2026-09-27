namespace ERP.Core.DTOs.Shared;

public class OnlinePaymentDto
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public OnlinePaymentPurpose Purpose { get; set; }
    public OnlinePaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Description { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? CustomerName { get; set; }
    public string CheckoutUrl { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? VoucherId { get; set; }
    public DateTime? ConsumedAt { get; set; }
}

/// <summary>حالة الدفع لصفحة العميل بعد العودة من Paymob (بلا دخول): أقل قدر من البيانات.</summary>
public class PublicPaymentStatusDto
{
    public OnlinePaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SAR";
    public string Description { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string CompanyName { get; set; } = string.Empty;
}

public class CreateInvoicePaymentLinkDto
{
    public Guid InvoiceId { get; set; }
    /// <summary>فارغ = كامل المتبقي على الفاتورة.</summary>
    public decimal? Amount { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
}

public class CreatePosPaymentDto
{
    public decimal Amount { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
}

public class CreateSubscriptionCheckoutDto
{
    public SubscriptionPlanId PlanId { get; set; }
    public SubscriptionBillingCycle BillingCycle { get; set; }
}

public class PaymentGatewaySettingsDto
{
    public bool IsEnabled { get; set; }
    public string BaseUrl { get; set; } = "https://ksa.paymob.com";
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>هل حُفظ مفتاح سري؟ (لا يُعاد المفتاح نفسه).</summary>
    public bool HasSecretKey { get; set; }
    public bool HasHmacSecret { get; set; }
    public string IntegrationIds { get; set; } = string.Empty;
    public string SettlementAccountCode { get; set; } = "1113";
    /// <summary>رابط الإشعارات الذي يُضبط في لوحة Paymob.</summary>
    public string WebhookUrl { get; set; } = string.Empty;
}

public class UpdatePaymentGatewaySettingsDto
{
    public bool IsEnabled { get; set; }
    public string BaseUrl { get; set; } = "https://ksa.paymob.com";
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>فارغ = يبقى المفتاح المحفوظ.</summary>
    public string? SecretKey { get; set; }
    public string? HmacSecret { get; set; }
    public string IntegrationIds { get; set; } = string.Empty;
    public string SettlementAccountCode { get; set; } = "1113";
}
