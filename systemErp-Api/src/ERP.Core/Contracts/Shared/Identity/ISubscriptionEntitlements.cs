namespace ERP.Core.Contracts.Shared;

/// <summary>ما يحق للمنشأة الحالية استعماله: حالة الاشتراك الفعلية والوحدات المرخّصة. يُقرأ مرة لكل طلب.</summary>
public interface ISubscriptionEntitlements
{
    Task<EntitlementInfo> GetAsync(CancellationToken ct = default);
}

/// <param name="TrialEnded">انتهت الفترة التجريبية المجانية ولم يُسدَّد الاشتراك بعد.</param>
public record EntitlementInfo(bool HasSubscription, SubscriptionStatus Status, DateTime? ExpiryDate, string[] Modules, bool TrialEnded = false)
{
    public bool Allows(string module) => Modules.Contains(module);
}
