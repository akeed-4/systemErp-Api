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

    /// <summary>كارت الصنف: نوع الحركة (purchase, sales, sales_return, purchase_return, adjustment, transfer)؛ فارغ = الكل.</summary>
    public string? MovementType { get; set; }

    /// <summary>التقرير التجاري: نوع المستند (sales, purchase, sales_return, purchase_return)؛ فارغ = الكل.</summary>
    public string? DocType { get; set; }

    /// <summary>بداية اليوم التالي لـ DateTo: المستندات تحمل وقتها، فالفترة تشمل يوم نهايتها كاملاً بشرط Date &lt; هذه القيمة.</summary>
    public DateTime? EndExclusive() => DateTo?.Date.AddDays(1);
}
