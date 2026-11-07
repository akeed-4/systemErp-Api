using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// وحدات القياس لمكتبة المقاولات: تُطابق بالرمز (Code) أو الاختصار (Symbol) دون حساسية للحالة، وتُخزَّن بالرمز. التحويل داخل النوع
/// الواحد فقط: 1 من الوحدة = معاملها من وحدة الأساس.
/// </summary>
internal sealed class BoqUnits
{
    private readonly Dictionary<string, UnitOfMeasure> _byKey;

    private BoqUnits(IEnumerable<UnitOfMeasure> units)
    {
        _byKey = new Dictionary<string, UnitOfMeasure>(StringComparer.OrdinalIgnoreCase);
        var list = units.ToList();
        foreach (var u in list.Where(u => !string.IsNullOrWhiteSpace(u.Code))) _byKey[u.Code.Trim()] = u;
        foreach (var u in list.Where(u => !string.IsNullOrWhiteSpace(u.Symbol))) _byKey.TryAdd(u.Symbol.Trim(), u);
    }

    public static async Task<BoqUnits> LoadAsync(ErpDbContext db, CancellationToken ct)
        => new(await db.Set<UnitOfMeasure>().AsNoTracking().ToListAsync(ct));

    public UnitOfMeasure? Find(string? codeOrSymbol)
        => string.IsNullOrWhiteSpace(codeOrSymbol) ? null : _byKey.GetValueOrDefault(codeOrSymbol.Trim());

    /// <summary>رمز الوحدة المعرّفة، أو خطأ (ويبقى المُدخل كما هو).</summary>
    public string Resolve(string unit, string owner, List<string> errors)
    {
        var u = Find(unit);
        if (u != null) return u.Code;
        errors.Add(string.Format(Messages.BoqUnitUnknown, owner, unit));
        return unit;
    }

    /// <summary>الكمية بوحدة الهدف؛ null إن لم تُعرف إحدى الوحدتين أو اختلف نوعاهما.</summary>
    public decimal? Convert(decimal quantity, string from, string to)
    {
        var a = Find(from); var b = Find(to);
        if (a == null || b == null || !string.Equals(a.UnitType, b.UnitType, StringComparison.OrdinalIgnoreCase)) return null;
        return a.Code == b.Code ? quantity : quantity * Factor(a) / Factor(b);
    }

    private static decimal Factor(UnitOfMeasure u) => u.IsBaseUnit || u.ConversionFactor <= 0 ? 1m : u.ConversionFactor;
}

/// <summary>أصناف موارد المواد: الوحدة المخزنية وتكلفة الوحدة المرجعية (المعيارية، ثم المتوسط، ثم آخر شراء).</summary>
internal sealed class BoqProducts
{
    public sealed record Info(Guid Id, string Sku, string Name, string Unit, decimal Cost);

    private readonly Dictionary<Guid, Info> _byId;
    private BoqProducts(Dictionary<Guid, Info> byId) => _byId = byId;

    public static async Task<BoqProducts> LoadAsync(ErpDbContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (wanted.Count == 0) return new(new());
        var rows = await db.Set<Product>().AsNoTracking().Where(p => wanted.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.NameAr, p.Unit, p.StandardCost, p.AverageCost, p.LastPurchaseCost }).ToListAsync(ct);
        return new(rows.ToDictionary(p => p.Id, p => new Info(p.Id, p.Sku, p.NameAr, p.Unit,
            p.StandardCost is > 0 ? p.StandardCost.Value : p.AverageCost > 0 ? p.AverageCost : p.LastPurchaseCost)));
    }

    public Info? Find(Guid id) => _byId.GetValueOrDefault(id);
}
