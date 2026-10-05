using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// المدفوعات الإلكترونية عبر Paymob: روابط دفع الفواتير، الدفع في نقطة البيع، واشتراكات المنشآت.
/// لا يُعدّ أي دفع ناجحاً إلا بإشعار Paymob الموقَّع؛ الأثر المحاسبي/التفعيل يُنفَّذ مرة واحدة فقط.
/// </summary>
public class OnlinePaymentService : IOnlinePaymentService
{
    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPaymobClient _paymob;
    private readonly ISecretProtector _secrets;
    private readonly PaymobOptions _platform;
    private readonly IVoucherService _vouchers;
    private readonly ITransactionRunner _tx;

    public OnlinePaymentService(ErpDbContext db, ITenantContext tenant, IPaymobClient paymob, ISecretProtector secrets,
        IOptions<PaymobOptions> platform, IVoucherService vouchers, ITransactionRunner tx)
    {
        _db = db; _tenant = tenant; _paymob = paymob; _secrets = secrets; _platform = platform.Value; _vouchers = vouchers; _tx = tx;
    }

    // ================= إنشاء عمليات الدفع =================
    public async Task<OnlinePaymentDto> CreateInvoicePaymentLinkAsync(CreateInvoicePaymentLinkDto r, CancellationToken ct = default)
    {
        var inv = await _db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.InvoiceId, ct) ?? throw new NotFoundException("الفاتورة غير موجودة");
        if (inv.Kind != InvoiceKind.Sales || inv.Status != "posted") throw new ConflictException("رابط الدفع لفاتورة مبيعات مرحّلة فقط.");
        if (inv.PartyId == null) throw new ValidationFailedException("الفاتورة بلا عميل مسجّل؛ لا يمكن ترحيل التحصيل على حسابه.");
        var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Id == inv.PartyId, ct);
        if (string.IsNullOrWhiteSpace(customer.AccountCode)) throw new ValidationFailedException("العميل غير مربوط بحساب في شجرة الحسابات.");

        var outstanding = await OutstandingAsync(inv, ct);
        if (outstanding <= 0) throw new ConflictException("الفاتورة مسددة بالكامل.");
        var amount = DocumentPricing.Round(r.Amount ?? outstanding);
        if (amount <= 0 || amount > outstanding) throw new ValidationFailedException($"المبلغ يجب أن يكون بين 0 و {outstanding:0.00}.");

        var creds = await TenantCredentialsAsync(ct);
        return await CreateAsync(new OnlinePayment
        {
            Purpose = OnlinePaymentPurpose.InvoicePayment, Amount = amount, Currency = inv.CurrencyCode,
            Description = $"سداد الفاتورة {inv.InvoiceNumber}", ReferenceId = inv.Id, ReferenceNumber = inv.InvoiceNumber,
            CustomerName = customer.NameAr, CustomerEmail = r.CustomerEmail ?? customer.Email, CustomerPhone = r.CustomerPhone ?? customer.Phone,
        }, creds, ct);
    }

    public async Task<OnlinePaymentDto> CreatePosPaymentAsync(CreatePosPaymentDto r, CancellationToken ct = default)
    {
        if (r.Amount <= 0) throw new ValidationFailedException("المبلغ يجب أن يكون موجباً.");
        var creds = await TenantCredentialsAsync(ct);
        return await CreateAsync(new OnlinePayment
        {
            Purpose = OnlinePaymentPurpose.PosSale, Amount = DocumentPricing.Round(r.Amount), Description = "دفع نقطة البيع",
            CustomerName = r.CustomerName, CustomerPhone = r.CustomerPhone, CustomerEmail = r.CustomerEmail,
        }, creds, ct);
    }

    public async Task<OnlinePaymentDto> CreateSubscriptionCheckoutAsync(CreateSubscriptionCheckoutDto r, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(r.PlanId) || !Enum.IsDefined(r.BillingCycle)) throw new ValidationFailedException("الباقة أو دورة الفوترة غير صالحة.");
        var plan = await SubscriptionCatalog.GetAsync(_db, r.PlanId, forSale: true, ct);
        var price = r.BillingCycle == SubscriptionBillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;
        var total = DocumentPricing.Round(price * (1 + SubscriptionCatalog.VatRate));
        var company = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        return await CreateAsync(new OnlinePayment
        {
            Purpose = OnlinePaymentPurpose.Subscription, Amount = total,
            Description = $"اشتراك {plan.NameAr} ({(r.BillingCycle == SubscriptionBillingCycle.Yearly ? "سنوي" : "شهري")})",
            PlanId = r.PlanId, BillingCycle = r.BillingCycle, CustomerName = company.NameAr, CustomerEmail = company.Email, CustomerPhone = company.Phone,
        }, PlatformCredentials(), ct);
    }

    private async Task<OnlinePaymentDto> CreateAsync(OnlinePayment p, PaymobCredentials creds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_platform.PublicApiUrl) || string.IsNullOrWhiteSpace(_platform.FrontendUrl))
            throw new ConflictException("اضبط Paymob:PublicApiUrl و Paymob:FrontendUrl في إعدادات الخادم أولاً.");
        p.Id = Guid.NewGuid();
        p.SpecialReference = $"ERP-{p.Id:N}";
        var intention = await _paymob.CreateIntentionAsync(creds, new PaymobIntentionRequest(
            (long)Math.Round(p.Amount * 100m, MidpointRounding.AwayFromZero), p.Currency, p.Description, p.SpecialReference,
            p.CustomerName, p.CustomerEmail, p.CustomerPhone,
            $"{_platform.PublicApiUrl.TrimEnd('/')}/api/v1/payments/paymob/webhook",
            $"{_platform.FrontendUrl.TrimEnd('/')}/payment-result?paymentId={p.Id}"), ct);
        p.ProviderIntentionId = intention.IntentionId;
        p.ProviderOrderId = intention.OrderId;
        p.ClientSecret = intention.ClientSecret;
        p.CheckoutUrl = intention.CheckoutUrl;
        _db.Add(p);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<OnlinePaymentDto>(p);
    }

    // ================= القراءة =================
    public async Task<OnlinePaymentDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<OnlinePaymentDto>(await _db.Set<OnlinePayment>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("عملية الدفع غير موجودة"));

    public async Task<List<OnlinePaymentDto>> ListAsync(Guid? referenceId, CancellationToken ct = default)
    {
        var q = _db.Set<OnlinePayment>().AsNoTracking();
        if (referenceId.HasValue) q = q.Where(p => p.ReferenceId == referenceId);
        return (await q.OrderByDescending(p => p.CreatedAt).Take(200).ToListAsync(ct)).Select(Mapper.Map<OnlinePaymentDto>).ToList();
    }

    public async Task<OnlinePaymentDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.Set<OnlinePayment>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("عملية الدفع غير موجودة");
        if (p.Status != OnlinePaymentStatus.Pending) throw new ConflictException("تُلغى عمليات الدفع المعلّقة فقط.");
        p.Status = OnlinePaymentStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<OnlinePaymentDto>(p);
    }

    /// <summary>صفحة العودة من Paymob (بلا دخول): المعرّف عشوائي غير قابل للتخمين، والمعروض أقل قدر ممكن.</summary>
    public async Task<PublicPaymentStatusDto> GetPublicStatusAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.Set<OnlinePayment>().IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("عملية الدفع غير موجودة");
        var company = await _db.Set<Tenant>().IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == p.TenantId).Select(t => t.NameAr).FirstOrDefaultAsync(ct);
        return new PublicPaymentStatusDto
        {
            Status = p.Status, Amount = p.Amount, Currency = p.Currency, Description = p.Description,
            ReferenceNumber = p.ReferenceNumber, CompanyName = company ?? string.Empty,
        };
    }

    // ================= الإشعار الموقَّع من Paymob =================
    public async Task<bool> HandleWebhookAsync(JsonElement payload, string? hmac, CancellationToken ct = default)
    {
        if (!payload.TryGetProperty("obj", out var obj) || obj.ValueKind != JsonValueKind.Object) return true; // ليس إشعار عملية
        if (payload.TryGetProperty("type", out var type) && type.GetString() is string t && !t.Equals("TRANSACTION", StringComparison.OrdinalIgnoreCase))
            return true;

        long? orderId = obj.TryGetProperty("order", out var order) && order.TryGetProperty("id", out var oid) && oid.ValueKind == JsonValueKind.Number ? oid.GetInt64() : null;
        var merchantRef = order.ValueKind == JsonValueKind.Object && order.TryGetProperty("merchant_order_id", out var mref) ? mref.ToString() : null;
        var specialRef = obj.TryGetProperty("special_reference", out var sref) ? sref.ToString() : null;

        var found = await _db.Set<OnlinePayment>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => (orderId != null && p.ProviderOrderId == orderId) || p.SpecialReference == merchantRef || p.SpecialReference == specialRef)
            .Select(p => new { p.Id, p.TenantId, p.Purpose }).FirstOrDefaultAsync(ct);
        if (found == null) return true; // عملية لا تخصنا: لا أثر

        var secret = found.Purpose == OnlinePaymentPurpose.Subscription
            ? _platform.HmacSecret
            : _secrets.Unprotect(await _db.Set<PaymentGatewaySettings>().IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.TenantId == found.TenantId).Select(s => s.HmacSecretEncrypted).FirstOrDefaultAsync(ct) ?? string.Empty);
        if (!PaymobHmac.Verify(obj, hmac, secret)) return false;

        _tenant.SetTenant(found.TenantId); // كل ما بعده ضمن منشأة الدفع
        await _tx.RunAsync(async token =>
        {
            var p = await _db.Set<OnlinePayment>().FirstAsync(x => x.Id == found.Id, token);
            if (p.Status == OnlinePaymentStatus.Paid) return; // إشعار مكرر: لا أثر مرتين

            var success = Bool(obj, "success") && !Bool(obj, "pending") && !Bool(obj, "is_voided") && !Bool(obj, "is_refunded");
            var cents = obj.TryGetProperty("amount_cents", out var ac) && ac.ValueKind == JsonValueKind.Number ? ac.GetInt64() : -1;
            var currency = obj.TryGetProperty("currency", out var cu) ? cu.GetString() : null;
            if (obj.TryGetProperty("id", out var txId) && txId.ValueKind == JsonValueKind.Number) p.ProviderTransactionId = txId.GetInt64();

            if (!success)
            {
                if (!Bool(obj, "pending"))
                {
                    p.Status = OnlinePaymentStatus.Failed;
                    p.FailureReason = obj.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("message", out var msg)
                        ? msg.ToString() : "رفض الدفع";
                }
                await _db.SaveChangesAsync(token);
                return;
            }
            if (cents != (long)Math.Round(p.Amount * 100m, MidpointRounding.AwayFromZero) || !string.Equals(currency, p.Currency, StringComparison.OrdinalIgnoreCase))
            {
                p.Status = OnlinePaymentStatus.Failed;
                p.FailureReason = $"المبلغ/العملة في الإشعار ({cents / 100m:0.00} {currency}) لا يطابق العملية.";
                await _db.SaveChangesAsync(token);
                return;
            }

            p.Status = OnlinePaymentStatus.Paid;
            p.PaidAt = DateTime.UtcNow;
            if (obj.TryGetProperty("source_data", out var src) && src.ValueKind == JsonValueKind.Object)
            {
                p.CardBrand = src.TryGetProperty("sub_type", out var st) ? st.ToString() : null;
                var pan = src.TryGetProperty("pan", out var pn) ? pn.ToString() : string.Empty;
                p.CardLast4 = pan.Length >= 4 ? pan[^4..] : null;
            }
            await _db.SaveChangesAsync(token);
            await ApplyEffectAsync(p, token);
            await _db.SaveChangesAsync(token);
        }, ct);
        return true;
    }

    /// <summary>أثر الدفع الناجح حسب الغرض.</summary>
    private async Task ApplyEffectAsync(OnlinePayment p, CancellationToken ct)
    {
        switch (p.Purpose)
        {
            case OnlinePaymentPurpose.InvoicePayment:
            {
                var inv = await _db.Set<Invoice>().AsNoTracking().FirstAsync(i => i.Id == p.ReferenceId, ct);
                var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Id == inv.PartyId, ct);
                var settlement = await SettlementAccountAsync(ct);
                var voucher = await _vouchers.CreateAsync(new CreateVoucherDto
                {
                    Type = VoucherType.Receipt, Date = DateTime.UtcNow, Amount = p.Amount,
                    PartyName = customer.NameAr, PartyAccountCode = customer.AccountCode, TreasuryAccountCode = settlement,
                    PaymentMethod = "bank_card", ReferenceNumber = inv.InvoiceNumber,
                    Notes = $"تحصيل إلكتروني عبر Paymob للفاتورة {inv.InvoiceNumber} (عملية {p.ProviderTransactionId})",
                    ReceivedOrPaidBy = "Paymob",
                }, ct);
                p.VoucherId = voucher.Id;
                break;
            }
            case OnlinePaymentPurpose.Subscription:
            {
                foreach (var s in await _db.Set<Subscription>().Where(s => s.Status == SubscriptionStatus.Active).ToListAsync(ct))
                    s.Status = SubscriptionStatus.Expired;
                var sub = SubscriptionCatalog.NewSubscription(await SubscriptionCatalog.GetAsync(_db, p.PlanId!.Value, forSale: false, ct), p.BillingCycle!.Value, "Paymob");
                sub.TransactionReference = p.ProviderTransactionId?.ToString() ?? p.SpecialReference;
                _db.Add(sub);
                p.ReferenceId = sub.Id;
                break;
            }
            case OnlinePaymentPurpose.PosSale:
                break; // يُستهلك عند إتمام البيع (ConsumeForPosAsync)
        }
    }

    // ================= إعدادات المنشأة =================
    public async Task<PaymentGatewaySettingsDto> GetSettingsAsync(CancellationToken ct = default)
        => ToDto(await _db.Set<PaymentGatewaySettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new PaymentGatewaySettings());

    public async Task<PaymentGatewaySettingsDto> UpdateSettingsAsync(UpdatePaymentGatewaySettingsDto r, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(r.BaseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationFailedException("عنوان Paymob يجب أن يكون https (مثل https://ksa.paymob.com).");
        var ids = ParseIds(r.IntegrationIds);
        var s = await _db.Set<PaymentGatewaySettings>().FirstOrDefaultAsync(ct);
        if (s == null) { s = new PaymentGatewaySettings(); _db.Add(s); }
        s.BaseUrl = r.BaseUrl.TrimEnd('/');
        s.PublicKey = r.PublicKey.Trim();
        if (!string.IsNullOrWhiteSpace(r.SecretKey)) s.SecretKeyEncrypted = _secrets.Protect(r.SecretKey.Trim());
        if (!string.IsNullOrWhiteSpace(r.HmacSecret)) s.HmacSecretEncrypted = _secrets.Protect(r.HmacSecret.Trim());
        s.IntegrationIds = string.Join(",", ids);
        s.SettlementAccountCode = string.IsNullOrWhiteSpace(r.SettlementAccountCode) ? DefaultAccounts.PaymentGatewayReceivable : r.SettlementAccountCode.Trim();
        if (r.IsEnabled && (string.IsNullOrEmpty(s.PublicKey) || string.IsNullOrEmpty(s.SecretKeyEncrypted) || string.IsNullOrEmpty(s.HmacSecretEncrypted) || ids.Count == 0))
            throw new ValidationFailedException("للتفعيل: المفتاح العام والسري ومفتاح HMAC ورقم تكامل واحد على الأقل.");
        s.IsEnabled = r.IsEnabled;
        await _db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    private PaymentGatewaySettingsDto ToDto(PaymentGatewaySettings s) => new()
    {
        IsEnabled = s.IsEnabled, BaseUrl = s.BaseUrl, PublicKey = s.PublicKey,
        HasSecretKey = !string.IsNullOrEmpty(s.SecretKeyEncrypted), HasHmacSecret = !string.IsNullOrEmpty(s.HmacSecretEncrypted),
        IntegrationIds = s.IntegrationIds, SettlementAccountCode = s.SettlementAccountCode,
        WebhookUrl = string.IsNullOrWhiteSpace(_platform.PublicApiUrl) ? string.Empty : $"{_platform.PublicApiUrl.TrimEnd('/')}/api/v1/payments/paymob/webhook",
    };

    // ================= مساعدات =================
    private async Task<PaymobCredentials> TenantCredentialsAsync(CancellationToken ct)
    {
        var s = await _db.Set<PaymentGatewaySettings>().AsNoTracking().FirstOrDefaultAsync(ct);
        if (s is not { IsEnabled: true }) throw new ConflictException("بوابة الدفع Paymob غير مفعّلة للمنشأة؛ اضبطها من إعدادات الدفع الإلكتروني.");
        return new PaymobCredentials(s.BaseUrl, _secrets.Unprotect(s.SecretKeyEncrypted), s.PublicKey, _secrets.Unprotect(s.HmacSecretEncrypted), ParseIds(s.IntegrationIds));
    }

    private PaymobCredentials PlatformCredentials()
    {
        var ids = ParseIds(_platform.IntegrationIds);
        if (string.IsNullOrWhiteSpace(_platform.SecretKey) || string.IsNullOrWhiteSpace(_platform.PublicKey) || ids.Count == 0)
            throw new ConflictException("دفع الاشتراكات غير مهيأ على المنصة (Paymob:SecretKey/PublicKey/IntegrationIds).");
        return new PaymobCredentials(_platform.BaseUrl, _platform.SecretKey, _platform.PublicKey, _platform.HmacSecret, ids);
    }

    /// <summary>حساب تحصيلات بوابة الدفع للمنشأة (يُنشأ 1113 إن لم يوجد).</summary>
    private Task<string> SettlementAccountAsync(CancellationToken ct) => SettlementAccountAsync(_db, ct);

    internal static async Task<string> SettlementAccountAsync(ErpDbContext db, CancellationToken ct)
    {
        var code = await db.Set<PaymentGatewaySettings>().AsNoTracking().Select(s => s.SettlementAccountCode).FirstOrDefaultAsync(ct)
            ?? DefaultAccounts.PaymentGatewayReceivable;
        if (code == DefaultAccounts.PaymentGatewayReceivable) await DefaultAccounts.EnsureAsync(db, ct, code);
        return code;
    }

    private async Task<decimal> OutstandingAsync(Invoice inv, CancellationToken ct)
    {
        var receipts = await _db.Set<Voucher>().AsNoTracking()
            .Where(v => v.Type == VoucherType.Receipt && v.ReferenceNumber == inv.InvoiceNumber).SumAsync(v => (decimal?)v.Amount, ct) ?? 0;
        return DocumentPricing.Round(inv.GrandTotal - receipts);
    }

    private static List<long> ParseIds(string? csv)
        => (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => long.TryParse(x, out var n) ? n : throw new ValidationFailedException($"رقم تكامل غير صالح: {x}")).ToList();

    private static bool Bool(JsonElement obj, string prop) => obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
}
