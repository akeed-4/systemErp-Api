namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// لوحة التأجير: الأسطول (القابل للتأجير والمؤجَّر والمحجوز والصيانة والاستغلال)، والعقود والحجوزات، وتسليمات وإرجاعات اليوم والمتأخر عن
/// الإرجاع، والنقل، والكشوف والرسوم المعلّقة، والمفوتر هذا الشهر وأعمدة آخر 14 يوماً، والاستغلال بالفئة.
/// </summary>
public class RentalDashboardDto
{
    public int FleetCount { get; set; }
    public int OutCount { get; set; }
    public int ReservedCount { get; set; }
    public int MaintenanceCount { get; set; }
    public int AvailableCount { get; set; }
    /// <summary>المؤجَّر ÷ القابل للتأجير (فارغ بلا أسطول).</summary>
    public decimal? UtilizationPercent { get; set; }
    public int ActiveContracts { get; set; }
    public int DraftContracts { get; set; }
    public int OpenBookings { get; set; }
    /// <summary>حجوزات معلّقة ينتهي تعليقها خلال 24 ساعة.</summary>
    public int ExpiringHolds { get; set; }
    public int DeliveriesToday { get; set; }
    public int ReturnsToday { get; set; }
    public int OverdueReturns { get; set; }
    public int TransportToday { get; set; }
    public int SubmittedTimesheets { get; set; }
    public int DraftTimesheets { get; set; }
    public int PendingCharges { get; set; }
    public decimal PendingChargesAmount { get; set; }
    /// <summary>كشوف معتمدة لم تُفوتر بعد (مبلغها قبل الضريبة).</summary>
    public decimal UnbilledApprovedAmount { get; set; }
    public decimal BilledThisMonth { get; set; }
    public int BillingsThisMonth { get; set; }
    public List<RentalDashboardMoveDto> Today { get; set; } = new();
    public List<RentalDashboardMoveDto> Overdue { get; set; } = new();
    public List<RentalCategoryUseDto> Categories { get; set; } = new();
    public List<RentalDayAmountDto> Trend { get; set; } = new();
}

/// <summary>حركة معدة: تسليم أو إرجاع مستحق، أو مهمة نقل.</summary>
public class RentalDashboardMoveDto
{
    /// <summary><c>delivery</c> أو <c>return</c> أو <c>transport</c>.</summary>
    public string Kind { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? EquipmentName { get; set; }
    public string? EquipmentCode { get; set; }
    public DateTime At { get; set; }
    public int DaysLate { get; set; }
    public string? Reference { get; set; }
}

public class RentalCategoryUseDto
{
    public Guid? CategoryId { get; set; }
    public string? NameAr { get; set; }
    public string? NameEn { get; set; }
    public int Fleet { get; set; }
    public int Out { get; set; }
}

public class RentalDayAmountDto
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
}
