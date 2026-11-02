namespace ERP.Core.DTOs.CarShowroom;

public class CarReportQueryDto
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    /// <summary>daily | monthly (لتقرير المبيعات الدوري).</summary>
    public string Period { get; set; } = "monthly";
    /// <summary>معرض/مستودع (فارغ = كل المعارض): مخزون السيارات بمعرضها، والمبيعات بمعرض فاتورتها، والأوامر بمعرض استلامها.</summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>بداية اليوم التالي لـ DateTo: الفترة تشمل يوم نهايتها كاملاً بشرط Date &lt; هذه القيمة.</summary>
    public DateTime? EndExclusive() => DateTo?.Date.AddDays(1);
}
