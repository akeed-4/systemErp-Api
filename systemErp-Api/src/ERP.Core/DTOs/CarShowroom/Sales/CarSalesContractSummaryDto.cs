namespace ERP.Core.DTOs.CarShowroom;

/// <summary>
/// إجماليات عقود بيع السيارات على كامل النتيجة المرشّحة (نفس مرشّحات القائمة)، محسوبة في SQL.
/// <list type="bullet">
/// <item>Count = عدد العقود المطابقة.</item>
/// <item>NetBeforeVat = Σ (NetPriceBeforeVat ?? SellingPrice) — صافي سعر البيع قبل الضريبة.</item>
/// <item>VatAmount = Σ VatAmount، TotalWithVat = Σ TotalWithVat (شامل الضريبة).</item>
/// <item>CostPrice = Σ CostPrice، ProfitMargin = Σ ProfitMargin كما يخزّنهما العقد.</item>
/// </list>
/// </summary>
public class CarSalesContractSummaryDto
{
    public int Count { get; set; }
    public decimal NetBeforeVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }
    public decimal CostPrice { get; set; }
    public decimal ProfitMargin { get; set; }
}
