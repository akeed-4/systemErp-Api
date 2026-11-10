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
/// مؤشرات لوحة التحكم، محسوبة في SQL على <b>الفواتير المرحّلة فقط</b> (Status = posted) لكل الفترات.
/// القيم "الصافية" تطرح المرتجعات من أصلها، والمبالغ بلا ضريبة (Subtotal) ما لم يُذكر خلاف ذلك.
/// <list type="table">
/// <item><term>NetSales</term><description>Σ Subtotal(مبيعات) − Σ Subtotal(مرتجعات مبيعات).</description></item>
/// <item><term>NetCostOfSales</term><description>Σ TotalCost(مبيعات) − Σ TotalCost(مرتجعات مبيعات).</description></item>
/// <item><term>GrossProfit</term><description>NetSales − NetCostOfSales.</description></item>
/// <item><term>NetOutputVat</term><description>Σ VatTotal(مبيعات) − Σ VatTotal(مرتجعات مبيعات).</description></item>
/// <item><term>NetPurchases</term><description>Σ Subtotal(مشتريات) − Σ Subtotal(مرتجعات مشتريات).</description></item>
/// <item><term>NetPurchasesWithVat</term><description>Σ GrandTotal(مشتريات) − Σ GrandTotal(مرتجعات مشتريات) (شامل الضريبة).</description></item>
/// <item><term>NetInputVat</term><description>Σ VatTotal(مشتريات) − Σ VatTotal(مرتجعات مشتريات).</description></item>
/// <item><term>NetCashPurchases / NetCreditPurchases</term><description>NetPurchases مقسّمة حسب طريقة سداد المستند نفسه: آجل (Credit) مقابل غير آجل.</description></item>
/// <item><term>SalesCount / SalesReturnsCount / PurchasesCount / PurchaseReturnsCount</term><description>عدد المستندات المرحّلة من كل نوع.</description></item>
/// <item><term>VouchersCount</term><description>عدد السندات. <term>PostedJournalEntriesCount</term> عدد القيود المرحّلة.</description></item>
/// <item><term>Monthly</term><description>نفس صيغ NetSales و GrossProfit و NetPurchases لكل شهر تقويمي حسب IssueDate، لآخر N شهر بما فيها الحالي (الأشهر الخالية = 0).</description></item>
/// </list>
/// </summary>
public class DashboardSummaryDto
{
    public decimal NetSales { get; set; }
    public decimal NetCostOfSales { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal NetOutputVat { get; set; }
    public decimal NetPurchases { get; set; }
    public decimal NetPurchasesWithVat { get; set; }
    public decimal NetInputVat { get; set; }
    public decimal NetCashPurchases { get; set; }
    public decimal NetCreditPurchases { get; set; }
    public int SalesCount { get; set; }
    public int SalesReturnsCount { get; set; }
    public int PurchasesCount { get; set; }
    public int PurchaseReturnsCount { get; set; }
    public int VouchersCount { get; set; }
    public int PostedJournalEntriesCount { get; set; }
    public List<DashboardMonthDto> Monthly { get; set; } = new();
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
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal NetSales { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal NetPurchases { get; set; }
}
