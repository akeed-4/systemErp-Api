using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// وحدة سطر المستند ومعاملها إلى وحدة الصنف الأساسية، والخادم مصدرها لا ما يرسله العميل. مشتركة بين الفواتير (البيع والشراء
/// ومرتجعاتهما بلا أصل) وعروض الأسعار والأوامر وبيانات التسليم، فتنتقل الوحدة نفسها من المستند إلى فاتورته.
/// - الوحدة المرسلة (برمزها أو اسمها أو رمزها المختصر في الدليل) من وحدات الصنف: الأساسية بمعامل 1 أو بديلة بمعاملها.
/// - الفارغة = الأساسية. صنف له وحدات بديلة يُرفض فيه غيرها (<c>InvoiceUnitNotLinked</c>)، إلا مع <c>unlinkedAsBase</c> للمستندات
///   التي لا تحرك المخزون: النص غير المرتبط (سطر قديم أو وحدة عقد وصفية) = الأساسية كما كان معناه قبل الوحدات البديلة.
/// - صنف بلا وحدات بديلة أو بند بلا صنف: <c>Unit</c> فارغ في الناتج = يُترك منطق الوحدة القائم (النص المرسل بمعامل 1).
/// </summary>
internal static class ProductLineUnits
{
    public static async Task<List<(string? Unit, decimal Factor)>> ResolveAsync(
        ErpDbContext db, IReadOnlyList<(Guid ItemId, string? Unit)> lines, CancellationToken ct, bool unlinkedAsBase = false)
    {
        var result = lines.Select(_ => ((string?)null, 1m)).ToList();
        var ids = lines.Select(l => l.ItemId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return result;

        var alternatives = (await db.Set<ProductUnit>().AsNoTracking().Where(u => ids.Contains(u.ProductId)).ToListAsync(ct))
            .GroupBy(u => u.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        if (alternatives.Count == 0) return result; // لا صنف بوحدات بديلة: السلوك القائم
        var withUnits = alternatives.Keys.ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => withUnits.Contains(p.Id))
            .Select(p => new { p.Id, p.Unit, p.NameAr }).ToDictionaryAsync(p => p.Id, ct);

        var codes = alternatives.Values.SelectMany(l => l.Select(u => u.UnitCode))
            .Concat(products.Values.Select(p => p.Unit)).Distinct().ToList();
        var catalogue = await db.Set<UnitOfMeasure>().AsNoTracking().Where(u => codes.Contains(u.Code))
            .Select(u => new { u.Code, u.NameAr, u.NameEn, u.Symbol }).ToListAsync(ct);
        bool Same(string code, string requested)
            => string.Equals(code, requested, StringComparison.OrdinalIgnoreCase)
               || catalogue.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)
                   && (string.Equals(c.NameAr, requested, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(c.NameEn, requested, StringComparison.OrdinalIgnoreCase)
                       || (!string.IsNullOrEmpty(c.Symbol) && string.Equals(c.Symbol, requested, StringComparison.OrdinalIgnoreCase))));

        for (var i = 0; i < lines.Count; i++)
        {
            var (itemId, unitText) = lines[i];
            if (!products.TryGetValue(itemId, out var p) || !alternatives.TryGetValue(itemId, out var units)) continue;
            var requested = unitText?.Trim() ?? string.Empty;
            if (requested.Length == 0 || Same(p.Unit, requested)) { result[i] = (p.Unit, 1m); continue; }
            var unit = units.FirstOrDefault(u => Same(u.UnitCode, requested));
            if (unit == null && unlinkedAsBase) { result[i] = (p.Unit, 1m); continue; }
            if (unit == null)
                throw new ValidationFailedException(string.Format(Messages.InvoiceUnitNotLinked, p.NameAr, requested));
            result[i] = (unit.UnitCode, unit.Factor);
        }
        return result;
    }

    /// <summary>
    /// للمستندات التي لا تحرك المخزون (عرض السعر، الأمر، بيان التسليم): يتحقق من وحدة كل سطر ويوحّدها إلى رمزها في وحدات الصنف،
    /// فتنتقل كما هي إلى الفاتورة عند التحويل ويحدد محرك الفواتير معاملها؛ النص غير المرتبط يصير الأساسية.
    /// </summary>
    public static async Task NormalizeAsync<TLine>(ErpDbContext db, IReadOnlyList<TLine> lines,
        Func<TLine, Guid> itemId, Func<TLine, string?> getUnit, Action<TLine, string> setUnit, CancellationToken ct)
    {
        var resolved = await ResolveAsync(db, lines.Select(l => (itemId(l), getUnit(l))).ToList(), ct, unlinkedAsBase: true);
        for (var i = 0; i < lines.Count; i++)
            if (resolved[i].Unit is { } code) setUnit(lines[i], code);
    }
}
