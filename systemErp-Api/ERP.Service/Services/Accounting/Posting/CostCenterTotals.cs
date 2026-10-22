namespace ERP.Service.Services.Accounting;

/// <summary>مجاميع مبالغ لكل مركز تكلفة، ومنها «بلا مركز»، لتوزيع قيد المستند على مراكز أسطره.</summary>
internal sealed class CostCenterTotals
{
    private readonly Dictionary<Guid, decimal> _amounts = new(); // Guid.Empty = بلا مركز (لا مركز حقيقياً بهذا المعرّف)

    private static Guid Key(Guid? center) => center ?? Guid.Empty;
    private static Guid? Center(Guid key) => key == Guid.Empty ? null : key;

    public void Add(Guid? center, decimal amount) => _amounts[Key(center)] = _amounts.GetValueOrDefault(Key(center)) + amount;
    public decimal this[Guid? center] => _amounts.GetValueOrDefault(Key(center));
    public IEnumerable<Guid?> Centers => _amounts.Keys.Select(Center);
    public decimal Total => _amounts.Values.Sum();

    public CostCenterTotals Copy()
    {
        var copy = new CostCenterTotals();
        foreach (var (key, amount) in _amounts) copy._amounts[key] = amount;
        return copy;
    }

    public List<CostCenterAmount> ToAmounts() => _amounts.Select(p => new CostCenterAmount(Center(p.Key), p.Value)).ToList();

    /// <summary>يوزّع مبلغاً على المراكز بنسبة أوزانها؛ كسر التقريب على أكبرها وزناً فيطابق مجموعُ الحصص المبلغَ تماماً.</summary>
    public static CostCenterTotals Apportion(decimal amount, CostCenterTotals weights)
    {
        var result = new CostCenterTotals();
        if (amount == 0) return result;
        var total = weights.Total;
        if (total == 0)
        {
            result.Add(weights.Centers.FirstOrDefault(), amount);
            return result;
        }
        foreach (var (key, weight) in weights._amounts)
            result._amounts[key] = Math.Round(amount * weight / total, 2, MidpointRounding.AwayFromZero);
        var largest = weights._amounts.MaxBy(p => Math.Abs(p.Value)).Key;
        result._amounts[largest] += amount - result.Total;
        return result;
    }
}
