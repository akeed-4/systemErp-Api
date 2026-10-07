namespace ERP.Core.DTOs.Shared;

public class SubscriptionPlanDto
{
    public SubscriptionPlanId Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescriptionAr { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string BadgeAr { get; set; } = string.Empty;
    public string BadgeEn { get; set; } = string.Empty;
    public bool IsPopular { get; set; }
    public string[] FeaturesAr { get; set; } = Array.Empty<string>();
    public string[] FeaturesEn { get; set; } = Array.Empty<string>();
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    /// <summary>null = غير محدود.</summary>
    public int? MaxUsers { get; set; }
    public int? MaxInvoicesPerMonth { get; set; }
    public int? Branches { get; set; }
    public bool ZatcaPhase2Enabled { get; set; } = true;
    public string[] Modules { get; set; } = Array.Empty<string>();
}
