namespace ERP.Core.DTOs.Accounting;

/// <summary>لوحة الخياطة: مؤشرات اليوم والشهر، وتوزيع القطع على المراحل، والأوامر المتأخرة، وبروفات اليوم.</summary>
public class TailoringDashboardDto
{
    public int OpenOrders { get; set; }
    public int LateOrders { get; set; }
    public int DueToday { get; set; }
    public int ReadyOrders { get; set; }
    public int ReadyPieces { get; set; }
    public int InProductionPieces { get; set; }
    public int QualityPieces { get; set; }
    public int OpenModifications { get; set; }
    public int FittingsToday { get; set; }
    public int DeliveriesThisMonth { get; set; }
    public decimal DeliveredThisMonth { get; set; }
    /// <summary>صافي الدفعات المقدمة غير المستردة على الأوامر القائمة.</summary>
    public decimal AdvancesOutstanding { get; set; }
    public List<TailoringStageCountDto> Stages { get; set; } = new();
    public List<TailoringLateOrderDto> Late { get; set; } = new();
    public List<TailoringFittingDto> Fittings { get; set; } = new();
}

public class TailoringStageCountDto
{
    public Guid? StageId { get; set; }
    /// <summary>للعمودين بلا مرحلة: <c>pending</c> و<c>ready</c>.</summary>
    public string Key { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? NameEn { get; set; }
    public int Count { get; set; }
}

public class TailoringLateOrderDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public DateTime DueDate { get; set; }
    public int DaysLate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
}
