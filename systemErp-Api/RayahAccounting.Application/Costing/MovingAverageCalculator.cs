namespace RayahAccounting.Application.Costing;

/// <summary>
/// خدمة حساب متوسط التكلفة المرجح المتحرك (Moving Weighted Average Cost)
/// عند كل حركة توريد أو شراء جديدة للمخزون
/// المعادلة المحاسبية:
/// New Average = ((الرصيد السابق * التكلفة السابقة) + (الكمية الواردة * سعر الشراء الجديد)) / (الرصيد السابق + الكمية الواردة)
/// </summary>
public static class MovingAverageCalculator
{
    public static decimal CalculateNewAverageCost(
        decimal currentStock,
        decimal currentAverageCost,
        decimal incomingQty,
        decimal incomingUnitPrice)
    {
        if (incomingQty <= 0)
            return currentAverageCost;

        if (currentStock <= 0)
            return Math.Round(incomingUnitPrice, 2);

        var existingStockValue = currentStock * currentAverageCost;
        var incomingStockValue = incomingQty * incomingUnitPrice;
        var totalStock = currentStock + incomingQty;

        if (totalStock <= 0)
            return incomingUnitPrice;

        var weightedAverage = (existingStockValue + incomingStockValue) / totalStock;
        return Math.Round(weightedAverage, 2);
    }
}
