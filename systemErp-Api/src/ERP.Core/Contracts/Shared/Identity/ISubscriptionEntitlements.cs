namespace ERP.Core.Contracts.Shared;

/// <summary>ما يحق للمنشأة الحالية استعماله: حالة الاشتراك الفعلية والوحدات المرخّصة. يُقرأ مرة لكل طلب.</summary>
public interface ISubscriptionEntitlements
{
    Task<EntitlementInfo> GetAsync(CancellationToken ct = default);
}

public record EntitlementInfo(bool HasSubscription, SubscriptionStatus Status, DateTime? ExpiryDate, string[] Modules)
{
    public bool Allows(string module) => Modules.Contains(module);
}
