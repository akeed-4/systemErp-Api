using System.Text.Json;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IOnlinePaymentService
{
    /// <summary>رابط دفع Paymob لفاتورة مبيعات مرحّلة (كامل المتبقي أو جزء منه). عند الدفع يُنشأ سند قبض آلياً.</summary>
    Task<OnlinePaymentDto> CreateInvoicePaymentLinkAsync(CreateInvoicePaymentLinkDto request, CancellationToken ct = default);
    /// <summary>دفع إلكتروني لسلة نقطة البيع؛ يُستهلك عند إتمام البيع بـ OnlinePaymentId.</summary>
    Task<OnlinePaymentDto> CreatePosPaymentAsync(CreatePosPaymentDto request, CancellationToken ct = default);
    /// <summary>دفع اشتراك المنشأة لحساب المنصة؛ تُفعَّل الباقة عند نجاح الدفع.</summary>
    Task<OnlinePaymentDto> CreateSubscriptionCheckoutAsync(CreateSubscriptionCheckoutDto request, CancellationToken ct = default);

    Task<OnlinePaymentDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<List<OnlinePaymentDto>> ListAsync(Guid? referenceId, CancellationToken ct = default);
    Task<OnlinePaymentDto> CancelAsync(Guid id, CancellationToken ct = default);
    Task<PublicPaymentStatusDto> GetPublicStatusAsync(Guid id, CancellationToken ct = default);

    /// <summary>إشعار Paymob الموقَّع (بلا دخول): يتحقق من HMAC والمبلغ وينفّذ الأثر مرة واحدة. false = توقيع غير صالح.</summary>
    Task<bool> HandleWebhookAsync(JsonElement payload, string? hmac, CancellationToken ct = default);

    Task<PaymentGatewaySettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<PaymentGatewaySettingsDto> UpdateSettingsAsync(UpdatePaymentGatewaySettingsDto request, CancellationToken ct = default);
}

/// <summary>العميل الفعلي لواجهة Paymob (يُستبدل ببديل وهمي في الاختبارات).</summary>
public interface IPaymobClient
{
    Task<PaymobIntention> CreateIntentionAsync(PaymobCredentials credentials, PaymobIntentionRequest request, CancellationToken ct = default);
}

public record PaymobCredentials(string BaseUrl, string SecretKey, string PublicKey, string HmacSecret, IReadOnlyList<long> IntegrationIds);
public record PaymobIntentionRequest(long AmountCents, string Currency, string Description, string SpecialReference,
    string? CustomerName, string? CustomerEmail, string? CustomerPhone, string NotificationUrl, string RedirectionUrl);
public record PaymobIntention(string IntentionId, string ClientSecret, long? OrderId, string CheckoutUrl);

/// <summary>تشفير الأسرار المحفوظة (مفاتيح بوابة الدفع).</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string cipher);
}
