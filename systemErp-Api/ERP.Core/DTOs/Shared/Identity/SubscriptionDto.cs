namespace ERP.Core.DTOs.Shared;

public partial class SubscriptionDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string PlanType { get; set; } = string.Empty;
    public string PlanNameAr { get; set; } = string.Empty;
    public string PlanNameEn { get; set; } = string.Empty;
    public SubscriptionBillingCycle BillingCycle { get; set; }
    public decimal Price { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? PromoCode { get; set; }
    public decimal DiscountAmount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public SubscriptionStatus Status { get; set; }
    public string PaymentMethod { get; set; } = "Mada";
    public string TransactionReference { get; set; } = string.Empty;
    public bool AutoRenew { get; set; } = true;
    public int MaxUsers { get; set; } = 10;
    public int MaxBranches { get; set; } = 3;
    public bool ZatcaPhase2Enabled { get; set; } = true;
    public string[] Modules { get; set; } = Array.Empty<string>();
    /// <summary>الميزات المتقدمة المشمولة (مفاتيح PlanFeatures).</summary>
    public string[] FeatureKeys { get; set; } = Array.Empty<string>();
    /// <summary>حد الفواتير الشهري (null = غير محدود).</summary>
    public int? MaxInvoicesPerMonth { get; set; }
    /// <summary>الأيام المتبقية حتى الانتهاء بتوقيت السرفر (سالبة بعد الانتهاء).</summary>
    public int DaysRemaining { get; set; }
    /// <summary>فترة تجريبية مجانية انتهت بلا سداد: النظام محجوب حتى يُدفع الاشتراك.</summary>
    public bool TrialEnded { get; set; }
}

/// <summary>استخدام الاشتراك مقابل حدوده: المستخدمون الفعّالون (بلا حسابات بوابة الموظف) والمستودعات الفعّالة مقابل الفروع.</summary>
public class SubscriptionUsageDto
{
    public int Users { get; set; }
    public int MaxUsers { get; set; }
    public int Warehouses { get; set; }
    public int MaxWarehouses { get; set; }
    /// <summary>فواتير المبيعات والمشتريات ومرتجعاتها المرحّلة هذا الشهر (بتوقيت المملكة).</summary>
    public int InvoicesThisMonth { get; set; }
    /// <summary>null = غير محدود. تجاوزه تنبيه فقط ولا يمنع الترحيل.</summary>
    public int? MaxInvoicesPerMonth { get; set; }
}
