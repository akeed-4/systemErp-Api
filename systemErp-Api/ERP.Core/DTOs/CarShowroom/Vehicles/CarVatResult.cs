namespace ERP.Core.DTOs.CarShowroom;

public record CarVatResult(decimal ProfitMargin, decimal VatAmount, decimal PriceWithVat, decimal ProfitMarginVat)
{
    /// <summary>صافي الإيراد قبل الضريبة. في نظام هامش الربح الضريبة مضمَّنة في السعر فيكون الصافي = السعر − الضريبة.</summary>
    public decimal NetBeforeVat => PriceWithVat - VatAmount;
}
