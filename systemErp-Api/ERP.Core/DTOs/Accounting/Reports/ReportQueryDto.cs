using ERP.Core.DTOs.Shared;

namespace ERP.Core.DTOs.Accounting;

public class ReportQueryDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public Guid? ItemId { get; set; }
    public string? Category { get; set; }
    /// <summary>تقارير المخزون: مستودع بعينه (تظهر تحويلاته)، وفارغ = المنشأة كلها.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>بداية اليوم التالي لـ DateTo: المستندات تحمل وقتها، فالفترة تشمل يوم نهايتها كاملاً بشرط Date &lt; هذه القيمة.</summary>
    public DateTime? EndExclusive() => DateTo?.Date.AddDays(1);
}
