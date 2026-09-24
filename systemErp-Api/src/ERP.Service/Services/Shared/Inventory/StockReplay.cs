namespace ERP.Service.Services.Shared;

/// <summary>
/// يعيد بناء رصيد صنف وتكلفته وطبقات FIFO من حركاته بترتيبها الزمني وفق سياسة التكلفة. يُستخدم بعد تعديل/حذف حركة
/// يدوية وفي إعادة احتساب التكلفة. لا يغيّر تكلفة حركات الصرف السابقة (تكلفة المبيعات المرحَّلة محاسبياً).
/// </summary>
public static class StockReplay
{
    /// <returns>أدنى رصيد وصل إليه الصنف أثناء الإعادة (لكشف المخزون السالب).</returns>
    public static decimal Apply(Product product, IReadOnlyList<StockMovement> ordered, CostingPolicy policy)
    {
        decimal stock = 0, avg = 0, last = 0, minStock = 0;
        var layers = new List<StockMovement>();

        foreach (var m in ordered)
        {
            if (m.Type is StockMovementType.InPurchase or StockMovementType.AdjustmentIn)
            {
                var baseStock = Math.Max(stock, 0);
                var newQty = baseStock + m.Quantity;
                switch (policy.Method)
                {
                    case CostingMethod.MovingAverage:
                    case CostingMethod.FIFO:
                        avg = newQty == 0 ? m.UnitCost : Math.Round((baseStock * avg + m.Quantity * m.UnitCost) / newQty, 4);
                        break;
                    case CostingMethod.LastPurchase:
                        avg = m.UnitCost;
                        break;
                }
                if (m.Type == StockMovementType.InPurchase) last = m.UnitCost;
                m.RemainingQuantity = m.Quantity;
                layers.Add(m);
                stock += m.Quantity;
            }
            else
            {
                var left = m.Quantity;
                foreach (var layer in layers)
                {
                    if (left <= 0) break;
                    var take = Math.Min(layer.RemainingQuantity, left);
                    layer.RemainingQuantity -= take; left -= take;
                }
                stock -= m.Quantity;
                if (policy.Method == CostingMethod.FIFO)
                {
                    var q = layers.Sum(l => l.RemainingQuantity);
                    if (q > 0) avg = Math.Round(layers.Sum(l => l.RemainingQuantity * l.UnitCost) / q, 4);
                }
                m.RemainingQuantity = 0;
            }
            m.RemainingStock = stock;
            minStock = Math.Min(minStock, stock);
        }

        if (policy.Method == CostingMethod.Standard && product.StandardCost.HasValue) avg = product.StandardCost.Value;
        product.CurrentStock = stock;
        product.AverageCost = avg;
        if (last > 0) product.LastPurchaseCost = last;
        return minStock;
    }
}
