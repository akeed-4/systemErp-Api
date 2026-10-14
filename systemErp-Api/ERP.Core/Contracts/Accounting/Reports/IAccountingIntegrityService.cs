using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>فحوص سلامة الدفاتر: تطابق الأرصدة المخزّنة مع القيود، وتطابق قيمة المخزون مع حساباته.</summary>
public interface IAccountingIntegrityService
{
    /// <summary>الحسابات التي يختلف رصيدها المخزَّن عن مجموع حركاتها (قراءة فقط).</summary>
    Task<BalanceIntegrityDto> CheckBalancesAsync(CancellationToken ct = default);
    /// <summary>يعيد رصيد كل حساب (والعميل/المورد/البنك المرتبط) إلى مجموع حركاته، ويُرجع ما أُصلح.</summary>
    Task<BalanceIntegrityDto> RebuildBalancesAsync(CancellationToken ct = default);
    /// <summary>قيمة المخزون من السجل التفصيلي مقابل أرصدة حسابات المخزون في الأستاذ.</summary>
    Task<InventoryReconciliationDto> GetInventoryReconciliationAsync(CancellationToken ct = default);
}
