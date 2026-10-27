namespace ERP.Core.DTOs.Shared;

/// <summary>رمز خصم كما يراه مسؤول المنصة، مع عدد استخداماته.</summary>
public class PromoCodeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "percent";
    public decimal Value { get; set; }
    public List<string> PlanIds { get; set; } = new();
    public SubscriptionBillingCycle? BillingCycle { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxRedemptions { get; set; }
    public int MaxPerTenant { get; set; } = 1;
    public bool IsActive { get; set; }
    public int Redemptions { get; set; }
    public decimal TotalDiscount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SavePromoCodeRequestDto
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = "percent";
    public decimal Value { get; set; }
    /// <summary>فارغة = كل الباقات.</summary>
    public List<string> PlanIds { get; set; } = new();
    public SubscriptionBillingCycle? BillingCycle { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public int? MaxRedemptions { get; set; }
    public int MaxPerTenant { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class SubscriptionQuoteRequestDto
{
    public string PlanId { get; set; } = string.Empty;
    public SubscriptionBillingCycle BillingCycle { get; set; }
    public string? PromoCode { get; set; }
}

/// <summary>تسعير الاشتراك قبل الدفع: السعر، خصم الرمز (قبل الضريبة)، الصافي، الضريبة، والإجمالي.</summary>
public class SubscriptionQuoteDto
{
    public string PlanId { get; set; } = string.Empty;
    public SubscriptionBillingCycle BillingCycle { get; set; }
    public decimal Price { get; set; }
    public decimal Discount { get; set; }
    public decimal Net { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public string? PromoCode { get; set; }
    public string? PromoDescription { get; set; }
}
