namespace ERP.Service.Services.Accounting;

/// <summary>
/// مجاميع مبالغ لكل شريحة (مركز تكلفة، موقع)، ومنها «بلا مركز» و«موقع الطلب»، لتوزيع قيد المستند على مراكز أسطره وحسابات مواقعها.
/// المستند بموقع واحد لكل أسطره يعطي شرائح بمركزها وحده كما كان.
/// </summary>
internal sealed class CostCenterTotals
{
    // Guid.Empty = بلا مركز / موقع الطلب (لا مركز ولا موقع حقيقياً بهذا المعرّف)
    private readonly Dictionary<(Guid Center, Guid Site), decimal> _amounts = new();

    private static (Guid, Guid) Key(Guid? center, Guid? site) => (center ?? Guid.Empty, site ?? Guid.Empty);
    private static Guid? Value(Guid key) => key == Guid.Empty ? null : key;

    public void Add(Guid? center, decimal amount, Guid? site = null)
    {
        var key = Key(center, site);
        _amounts[key] = _amounts.GetValueOrDefault(key) + amount;
    }

    public decimal this[(Guid? Center, Guid? Site) slice] => _amounts.GetValueOrDefault(Key(slice.Center, slice.Site));
    public IEnumerable<(Guid? Center, Guid? Site)> Slices => _amounts.Keys.Select(k => (Value(k.Center), Value(k.Site)));
    public decimal Total => _amounts.Values.Sum();

    public CostCenterTotals Copy()
    {
        var copy = new CostCenterTotals();
        foreach (var (key, amount) in _amounts) copy._amounts[key] = amount;
        return copy;
    }

    public List<CostCenterAmount> ToAmounts()
        => _amounts.Select(p => new CostCenterAmount(Value(p.Key.Center), p.Value, Value(p.Key.Site))).ToList();

    /// <summary>يوزّع مبلغاً على الشرائح بنسبة أوزانها؛ كسر التقريب على أكبرها وزناً فيطابق مجموعُ الحصص المبلغَ تماماً.</summary>
    public static CostCenterTotals Apportion(decimal amount, CostCenterTotals weights)
    {
        var result = new CostCenterTotals();
        if (amount == 0) return result;
        var total = weights.Total;
        if (total == 0)
        {
            var first = weights.Slices.FirstOrDefault();
            result.Add(first.Center, amount, first.Site);
            return result;
        }
        foreach (var (key, weight) in weights._amounts)
            result._amounts[key] = Math.Round(amount * weight / total, 2, MidpointRounding.AwayFromZero);
        var largest = weights._amounts.MaxBy(p => Math.Abs(p.Value)).Key;
        result._amounts[largest] += amount - result.Total;
        return result;
    }
}
