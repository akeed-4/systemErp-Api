namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// إقفال الفترات: لا قيد ولا حركة مخزون بتاريخ يقع في فترة مقفلة (حتى تاريخ إقفال الدفاتر شاملاً).
/// يستعمله المحرك المحاسبي ومحرك المخزون فلا يتجاوزه أي مستند.
/// </summary>
public interface IPeriodLock
{
    Task<bool> IsOpenAsync(DateTime date, CancellationToken ct = default);
    /// <summary>يرفض التاريخ الواقع في فترة مقفلة برسالة تحمل تاريخ الإقفال.</summary>
    Task EnsureOpenAsync(DateTime date, CancellationToken ct = default);
}
