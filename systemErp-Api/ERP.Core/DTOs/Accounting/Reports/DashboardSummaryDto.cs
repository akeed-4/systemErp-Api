namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// مؤشرات لوحة التحكم محسوبة في قاعدة البيانات: أعداد وإجماليات فقط، بدل تنزيل الفواتير والقيود والسندات كاملة إلى الواجهة.
/// المبالغ بالعملة الأساسية، والفواتير الملغاة مستبعدة، والمرتجعات بالسالب.
/// </summary>
public class DashboardSummaryDto
{
    public int SalesCount { get; set; }
    /// <summary>صافي المبيعات قبل الضريبة بعد المرتجعات.</summary>
    public decimal TotalSales { get; set; }
    public decimal TotalVatSales { get; set; }
    public int PurchasesCount { get; set; }
    /// <summary>إجمالي فواتير المشتريات شامل الضريبة.</summary>
    public decimal TotalPurchases { get; set; }
    public int CustomersCount { get; set; }
    public int SuppliersCount { get; set; }
    public int ProductsCount { get; set; }
    public int AccountsCount { get; set; }
    public int JournalEntriesCount { get; set; }
    public int VouchersCount { get; set; }
    /// <summary>آخر تسعة أشهر من الأقدم للأحدث، والشهر بلا حركة يظهر بأصفار.</summary>
    public List<DashboardMonthDto> Months { get; set; } = new();
    /// <summary>مؤشرات المعارض؛ فارغة لمنشأة بلا نشاط المعارض.</summary>
    public DashboardShowroomDto? Showroom { get; set; }
}

public class DashboardMonthDto
{
    /// <summary>yyyy-MM</summary>
    public string Month { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Profit { get; set; }
    public decimal Purchases { get; set; }
}

public class DashboardShowroomDto
{
    public int VehiclesTotal { get; set; }
    public int VehiclesAvailable { get; set; }
    public int VehiclesReserved { get; set; }
    public int VehiclesSold { get; set; }
    /// <summary>تكلفة السيارات في المخزون (المتاحة والمحجوزة).</summary>
    public decimal InventoryValuation { get; set; }
    public int ProcurementCount { get; set; }
    public int ContractsCount { get; set; }
    public decimal CashPurchases { get; set; }
    public decimal CreditPurchases { get; set; }
}

/// <summary>
/// أداء المبيعات لفترة (أسبوع/شهر/سنة حتى اليوم) وموقع: الصافي قبل الضريبة بعد المرتجعات، والتكلفة = الصافي − الربح الإجمالي
/// المخزّن على الفاتورة، والمقارنة بالمدة نفسها من الفترة السابقة. المبالغ بالعملة الأساسية، والملغاة مستبعدة.
/// </summary>
public class SalesPerformanceDto
{
    /// <summary>week | month | year.</summary>
    public string Period { get; set; } = "month";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime PreviousFrom { get; set; }
    public DateTime PreviousTo { get; set; }
    public Guid? WarehouseId { get; set; }
    public SalesPerformanceTotalsDto Current { get; set; } = new();
    public SalesPerformanceTotalsDto Previous { get; set; } = new();
    /// <summary>الاتجاه: أيام الأسبوع أو الشهر، أو أشهر السنة.</summary>
    public List<SalesPerformanceBucketDto> Trend { get; set; } = new();
    /// <summary>التوزيع على المواقع (بلا تقييد بموقع واحد)؛ المستند بلا موقع تحت «بلا موقع».</summary>
    public List<SalesPerformanceSiteDto> Sites { get; set; } = new();
    public List<SalesPerformancePartyDto> TopCustomers { get; set; } = new();
}

public class SalesPerformanceTotalsDto
{
    public decimal Sales { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
    /// <summary>هامش الربح % من الصافي (null بلا مبيعات).</summary>
    public decimal? MarginPercent { get; set; }
    public decimal Returns { get; set; }
    public decimal Vat { get; set; }
    public int InvoiceCount { get; set; }
    public decimal AverageInvoice { get; set; }
    public decimal Purchases { get; set; }
}

public class SalesPerformanceBucketDto
{
    /// <summary>yyyy-MM-dd لليوم، أو yyyy-MM للشهر.</summary>
    public string Key { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
}

public class SalesPerformanceSiteDto
{
    public Guid? WarehouseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public decimal Profit { get; set; }
    public int InvoiceCount { get; set; }
}

public class SalesPerformancePartyDto
{
    public string Name { get; set; } = string.Empty;
    public decimal Sales { get; set; }
    public int InvoiceCount { get; set; }
}
