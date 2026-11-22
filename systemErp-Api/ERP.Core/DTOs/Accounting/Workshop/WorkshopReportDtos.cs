namespace ERP.Core.DTOs.Accounting;

/// <summary>ربحية أمر عمل مفوتر: الإيراد قبل الضريبة مقابل التكلفة الفعلية (عمالة، قطع، خدمات خارجية).</summary>
public class WorkOrderProfitRowDto
{
    public Guid WorkOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoicedAt { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? PlateNumber { get; set; }
    public bool IsWarrantyJob { get; set; }
    public decimal Revenue { get; set; }
    public decimal LaborCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal ExternalCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Profit { get; set; }
    /// <summary>الهامش من الإيراد (فارغ بلا إيراد).</summary>
    public decimal? MarginPercent { get; set; }
}

/// <summary>أداء الفني في الفترة: الدقائق المسجلة (عمل وتوقف)، والساعات المعيارية المفوترة الموزعة عليه، والإيراد والتكلفة.</summary>
public class TechnicianPerformanceRowDto
{
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public decimal WorkMinutes { get; set; }
    public decimal IdleMinutes { get; set; }
    /// <summary>الساعات المعيارية لأسطر العمالة المفوترة بنسبة دقائقه منها.</summary>
    public decimal BilledHours { get; set; }
    /// <summary>الساعات المفوترة ÷ ساعات العمل المسجلة (فارغ بلا عمل).</summary>
    public decimal? EfficiencyPercent { get; set; }
    public decimal LaborRevenue { get; set; }
    public decimal LaborCost { get; set; }
    public List<IdleReasonDto> IdleReasons { get; set; } = new();
}

public class IdleReasonDto
{
    public string? Reason { get; set; }
    public decimal Minutes { get; set; }
}

/// <summary>قطع الغيار في الفترة: المصروف الصافي وتكلفته، والمفوتر منه وإيراده.</summary>
public class PartProfitRowDto
{
    public Guid ProductId { get; set; }
    public string? Sku { get; set; }
    public string? Name { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal IssuedCost { get; set; }
    public decimal BilledQuantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal Profit { get; set; }
}

/// <summary>لوحة الورشة الآن: الأوامر القائمة بحالاتها، والمتأخر عن موعده، وأقدمها، وأسباب التوقف، والفنيون العاملون الآن.</summary>
public class WorkshopDashboardDto
{
    public int OpenCount { get; set; }
    public int OverdueCount { get; set; }
    public int UrgentCount { get; set; }
    public decimal AverageAgeDays { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    /// <summary>عدد الأوامر المتوقفة لكل سبب.</summary>
    public Dictionary<string, int> PausedByReason { get; set; } = new();
    public List<WorkOrderDto> Overdue { get; set; } = new();
    public List<TimeLogDto> ActiveLogs { get; set; } = new();
}

public class WorkshopReportTotalsDto
{
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    public int Orders { get; set; }
}

public class WorkshopProfitabilityDto
{
    public WorkshopReportTotalsDto Totals { get; set; } = new();
    public List<WorkOrderProfitRowDto> Rows { get; set; } = new();
}
