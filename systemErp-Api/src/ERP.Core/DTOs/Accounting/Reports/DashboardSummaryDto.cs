namespace ERP.Core.DTOs.Accounting;

/// <summary>
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
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal NetSales { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal NetPurchases { get; set; }
}
