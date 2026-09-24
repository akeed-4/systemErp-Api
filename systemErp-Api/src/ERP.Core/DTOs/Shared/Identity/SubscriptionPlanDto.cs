namespace ERP.Core.DTOs.Shared;

public class SubscriptionPlanDto
{
    public SubscriptionPlanId Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceYearly { get; set; }
    /// <summary>null = غير محدود.</summary>
    public int? MaxUsers { get; set; }
    public int? MaxInvoicesPerMonth { get; set; }
    public int? Branches { get; set; }
}
