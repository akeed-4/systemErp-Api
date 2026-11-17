namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// توزيع الإهلاك الفعلي لمعدات «طريقة الإهلاك» على المشاريع (<see cref="EquipmentDepreciationAllocation"/>). كل استدعاء يعيد حساب
/// الفترة كاملة ويرحّل الفرق عمّا رُحِّل قبل (فهو آمن للتكرار)، ويُستدعى داخل معاملة المستدعي.
/// </summary>
public interface IEquipmentCostAllocationService
{
    /// <summary>يعيد توزيع إهلاك أصل عن فترة (yyyy-MM) على المشاريع؛ بلا إهلاك مرحّل أو بلا طريقة الإهلاك يُعكس أي توزيع سابق.</summary>
    Task ReallocateAsync(Guid fixedAssetId, string period, CancellationToken ct = default);

    /// <summary>بعد اعتماد تقرير يومي: يعيد توزيع فترة تاريخه لمعدات طريقة الإهلاك المستخدمة فيه.</summary>
    Task ReallocateForReportAsync(Guid reportId, CancellationToken ct = default);

    /// <summary>يعيد توزيع فترة لكل المعدات المرتبطة بأصول (إجراء يدوي بعد تصحيحات متأخرة)؛ يعيد عدد الأصول المعالجة.</summary>
    Task<int> ReallocatePeriodAsync(string period, CancellationToken ct = default);
}
