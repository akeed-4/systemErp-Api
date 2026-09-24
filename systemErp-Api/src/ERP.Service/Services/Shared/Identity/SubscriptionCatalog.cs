using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>كتالوج الباقات (ثابت كما في الواجهة: starter / professional / enterprise).</summary>
public static class SubscriptionCatalog
{
    public const decimal VatRate = 0.15m;

    public static readonly List<SubscriptionPlanDto> Plans = new()
    {
        new() { Id = SubscriptionPlanId.Starter, NameAr = "باقة البداية (Starter)", NameEn = "Starter Plan",
                PriceMonthly = 199, PriceYearly = 1990, MaxUsers = 2, MaxInvoicesPerMonth = 500, Branches = 1 },
        new() { Id = SubscriptionPlanId.Professional, NameAr = "باقة الشركات المتقدمة (Professional)", NameEn = "Professional Business Plan",
                PriceMonthly = 499, PriceYearly = 4990, MaxUsers = 10, MaxInvoicesPerMonth = null, Branches = 3 },
        new() { Id = SubscriptionPlanId.Enterprise, NameAr = "باقة المجموعات والمؤسسات (Enterprise)", NameEn = "Enterprise Corporate Plan",
                PriceMonthly = 999, PriceYearly = 9990, MaxUsers = null, MaxInvoicesPerMonth = null, Branches = null },
    };

    public static SubscriptionPlanDto Get(SubscriptionPlanId id) => Plans.First(p => p.Id == id);

    public static Subscription NewSubscription(SubscriptionPlanDto plan, SubscriptionBillingCycle cycle, string paymentMethod)
    {
        var price = cycle == SubscriptionBillingCycle.Yearly ? plan.PriceYearly : plan.PriceMonthly;
        var vat = Math.Round(price * VatRate, 2);
        var now = DateTime.UtcNow;
        return new Subscription
        {
            PlanType = plan.Id,
            PlanNameAr = plan.NameAr,
            PlanNameEn = plan.NameEn,
            BillingCycle = cycle,
            Price = price,
            VatAmount = vat,
            TotalAmount = price + vat,
            StartDate = now,
            ExpiryDate = cycle == SubscriptionBillingCycle.Yearly ? now.AddYears(1) : now.AddMonths(1),
            Status = SubscriptionStatus.Active,
            PaymentMethod = paymentMethod,
            TransactionReference = $"TXN-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            MaxUsers = plan.MaxUsers ?? int.MaxValue,
            MaxBranches = plan.Branches ?? int.MaxValue,
            ZatcaPhase2Enabled = plan.Id != SubscriptionPlanId.Starter,
        };
    }
}
