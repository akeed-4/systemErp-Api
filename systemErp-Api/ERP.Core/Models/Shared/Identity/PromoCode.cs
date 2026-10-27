namespace ERP.Core.Models.Shared;

public static class PromoDiscountTypes
{
    /// <summary>نسبة مئوية من سعر الباقة قبل الضريبة.</summary>
    public const string Percent = "percent";
    /// <summary>مبلغ ثابت يُخصم من سعر الباقة قبل الضريبة (لا يتجاوزه).</summary>
    public const string Fixed = "fixed";
}

/// <summary>
/// رمز خصم (برومو كود) على اشتراكات المنصة: يديره مسؤول المنصة، ويُخصم من سعر الباقة قبل الضريبة.
/// على مستوى المنصة (ليس تابعاً لمنشأة) مثل كتالوج الباقات.
/// </summary>
public class PromoCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>الرمز بأحرف كبيرة، فريد على المنصة.</summary>
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>percent | fixed</summary>
    public string DiscountType { get; set; } = PromoDiscountTypes.Percent;
    public decimal Value { get; set; }
    /// <summary>الباقات التي ينطبق عليها مفصولة بفواصل؛ فارغ = كل الباقات.</summary>
    public string? PlanIds { get; set; }
    /// <summary>دورة الفوترة التي ينطبق عليها؛ null = الشهرية والسنوية.</summary>
    public SubscriptionBillingCycle? BillingCycle { get; set; }
    public DateTime? ValidFrom { get; set; }
    /// <summary>آخر يوم صلاحية (شاملاً).</summary>
    public DateTime? ValidUntil { get; set; }
    /// <summary>أقصى عدد استخدامات على المنصة كلها؛ null = بلا حد.</summary>
    public int? MaxRedemptions { get; set; }
    /// <summary>أقصى عدد استخدامات للمنشأة الواحدة.</summary>
    public int MaxPerTenant { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>استخدام رمز خصم: يُسجَّل عند تفعيل الاشتراك المدفوع (أو المجاني بالكامل) لا عند فتح صفحة الدفع.</summary>
public class PromoCodeRedemption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PromoCodeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public string PlanId { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
}
