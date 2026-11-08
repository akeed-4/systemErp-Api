using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مكونات الأصناف (المجمّع والمركّب والمنتج الصناعي): التحقق والحفظ والقراءة بتكلفتها الحالية. مصدر واحد تستخدمه شاشة الأصناف
/// وفاتورة البيع (فك المجمّع/المركّب) ومستند التصنيع (قائمة المواد).
/// </summary>
public static class ProductComponents
{
    /// <summary>تكلفة وحدة الصنف الحالية للتقدير: المتوسط، ثم المعيارية، ثم آخر شراء؛ والخدمي بمعياريته.</summary>
    public static decimal UnitCostOf(Product p)
        => ProductItemTypes.Normalize(p.ItemType) == ProductItemTypes.Service
            ? p.StandardCost ?? 0
            : p.AverageCost > 0 ? p.AverageCost : p.StandardCost ?? p.LastPurchaseCost;

    /// <summary>مكونات الأصناف المطلوبة مرتبة، لكل صنف قائمته.</summary>
    public static async Task<Dictionary<Guid, List<ProductComponent>>> LoadAsync(ErpDbContext db, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        if (productIds.Count == 0) return new();
        return (await db.Set<ProductComponent>().AsNoTracking().Where(c => productIds.Contains(c.ProductId))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.CreatedAt).ToListAsync(ct))
            .GroupBy(c => c.ProductId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>مكونات صنف للعرض: بيانات الصنف المكوّن الحالية وتكلفة كل سطر لكل وحدة واحدة من المالك.</summary>
    public static async Task<(List<ProductComponentDto> Lines, decimal UnitCost)> DescribeAsync(ErpDbContext db, Guid productId, CancellationToken ct)
    {
        var rows = (await LoadAsync(db, new[] { productId }, ct)).GetValueOrDefault(productId) ?? new();
        var ids = rows.Where(r => r.ComponentProductId.HasValue).Select(r => r.ComponentProductId!.Value).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var lines = rows.Select(r =>
        {
            var dto = new ProductComponentDto
            {
                Id = r.Id, Kind = r.Kind, ComponentProductId = r.ComponentProductId, Quantity = r.Quantity, WastePercent = r.WastePercent,
                Name = r.Name, UnitCost = r.UnitCost, AccountCode = r.AccountCode, SortOrder = r.SortOrder,
            };
            if (r.Kind == ProductComponentKinds.Overhead)
            {
                dto.ComponentUnitCost = r.UnitCost;
                dto.LineCost = r.UnitCost;
            }
            else if (r.ComponentProductId is { } id && products.TryGetValue(id, out var p))
            {
                dto.ComponentSku = p.Sku; dto.ComponentName = p.NameAr; dto.ComponentUnit = p.Unit;
                dto.ComponentItemType = ProductItemTypes.Normalize(p.ItemType); dto.ComponentStock = p.CurrentStock;
                dto.ComponentUnitCost = UnitCostOf(p);
                dto.LineCost = Math.Round(r.Quantity * (1 + r.WastePercent / 100m) * dto.ComponentUnitCost, 4);
            }
            return dto;
        }).ToList();
        return (lines, Math.Round(lines.Sum(l => l.LineCost), 4));
    }

    /// <summary>
    /// يتحقق من مكونات صنف بطبيعته ويعيدها نظيفة: المجمّع والمركّب أصناف بضاعة (أو منتجات صناعية) فقط؛ والمنتج الصناعي مواد وخدمات
    /// ومصاريف غير مباشرة. الكمية موجبة، ولا يتكرر الصنف، ولا يدخل الصنف في نفسه ولا في دورة.
    /// </summary>
    public static async Task<List<ProductComponentDto>> ValidateAsync(ErpDbContext db, Guid? ownerId, string itemType, List<ProductComponentDto> input, CancellationToken ct)
    {
        var lines = input.Where(c => c.Kind == ProductComponentKinds.Overhead ? !string.IsNullOrWhiteSpace(c.Name) || c.UnitCost != 0 : c.ComponentProductId.HasValue).ToList();
        if (!ProductItemTypes.HasComponents(itemType))
        {
            if (lines.Count > 0) throw new ValidationFailedException("المكونات للصنف المجمّع أو المركّب أو المنتج الصناعي فقط.");
            return lines;
        }
        var isManufactured = itemType == ProductItemTypes.Manufactured;
        var items = lines.Where(l => l.Kind != ProductComponentKinds.Overhead).ToList();
        if (items.Count == 0)
            throw new ValidationFailedException(isManufactured ? "المنتج الصناعي يحتاج مادة أولية واحدة على الأقل في مكوناته." : "الصنف المجمّع أو المركّب يحتاج مكوّناً واحداً على الأقل.");

        foreach (var l in lines)
        {
            l.Kind = l.Kind == ProductComponentKinds.Overhead ? ProductComponentKinds.Overhead : ProductComponentKinds.Item;
            l.AccountCode = string.IsNullOrWhiteSpace(l.AccountCode) ? null : l.AccountCode.Trim();
            if (l.WastePercent is < 0 or >= 100) throw new ValidationFailedException("نسبة الهدر بين 0 وأقل من 100.");
            if (l.Kind == ProductComponentKinds.Overhead)
            {
                if (!isManufactured) throw new ValidationFailedException("المصاريف غير المباشرة للمنتج الصناعي فقط.");
                if (string.IsNullOrWhiteSpace(l.Name)) throw new ValidationFailedException("اسم المصروف غير المباشر مطلوب.");
                if (l.UnitCost <= 0) throw new ValidationFailedException($"مبلغ المصروف «{l.Name}» لكل وحدة يجب أن يكون موجباً.");
                l.Name = l.Name.Trim(); l.ComponentProductId = null; l.Quantity = 0; l.WastePercent = 0;
            }
            else
            {
                if (l.Quantity <= 0) throw new ValidationFailedException("كمية المكوّن يجب أن تكون موجبة.");
                if (l.ComponentProductId == ownerId) throw new ValidationFailedException("لا يدخل الصنف في مكونات نفسه.");
                l.Name = null; l.UnitCost = 0;
                if (!isManufactured) l.AccountCode = null;
            }
        }
        if (items.GroupBy(l => l.ComponentProductId).Any(g => g.Count() > 1)) throw new ValidationFailedException("المكوّن مكرر في مكونات الصنف.");

        var ids = items.Select(l => l.ComponentProductId!.Value).ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.NameAr, p.ItemType }).ToDictionaryAsync(p => p.Id, ct);
        foreach (var l in items)
        {
            if (!products.TryGetValue(l.ComponentProductId!.Value, out var p)) throw new ValidationFailedException(Messages.ProductNotFound);
            var type = ProductItemTypes.Normalize(p.ItemType);
            var allowed = isManufactured ? ProductItemTypes.TracksStock(type) || type == ProductItemTypes.Service : ProductItemTypes.TracksStock(type);
            if (!allowed)
                throw new ValidationFailedException(isManufactured
                    ? $"«{p.NameAr}» لا يدخل في تصنيع منتج: المكونات مواد بضاعة أو منتجات صناعية أو خدمات."
                    : $"«{p.NameAr}» ليس صنفاً له رصيد: مكونات المجمّع والمركّب أصناف بضاعة أو منتجات صناعية.");
        }

        var accounts = lines.Where(l => l.AccountCode != null).Select(l => l.AccountCode!).Distinct().ToList();
        if (accounts.Count > 0)
        {
            var found = await db.Set<Account>().AsNoTracking().Where(a => accounts.Contains(a.Code)).Select(a => a.Code).ToListAsync(ct);
            var missing = accounts.Except(found).FirstOrDefault();
            if (missing != null) throw new ValidationFailedException($"الحساب {missing} غير موجود في دليل الحسابات.");
        }

        // دورة: منتج يدخل في مكونات مكوّن له (مباشرة أو عبر منتجات صناعية أخرى)
        if (ownerId is { } owner)
        {
            var seen = new HashSet<Guid>();
            var frontier = ids.ToList();
            while (frontier.Count > 0)
            {
                if (frontier.Contains(owner)) throw new ValidationFailedException("المكونات تكوّن دورة: الصنف يدخل في مكونات أحد مكوناته.");
                frontier = await db.Set<ProductComponent>().AsNoTracking()
                    .Where(c => frontier.Contains(c.ProductId) && c.ComponentProductId != null)
                    .Select(c => c.ComponentProductId!.Value).Distinct().ToListAsync(ct);
                frontier = frontier.Where(seen.Add).ToList();
            }
        }
        for (var i = 0; i < lines.Count; i++) lines[i].SortOrder = i;
        return lines;
    }

    /// <summary>يستبدل مكونات الصنف بالقائمة المتحقق منها.</summary>
    public static async Task ReplaceAsync(ErpDbContext db, Guid productId, List<ProductComponentDto> lines, CancellationToken ct)
    {
        var existing = await db.Set<ProductComponent>().Where(c => c.ProductId == productId).ToListAsync(ct);
        db.RemoveRange(existing);
        foreach (var l in lines)
            db.Add(new ProductComponent
            {
                ProductId = productId, Kind = l.Kind, ComponentProductId = l.ComponentProductId, Quantity = l.Quantity,
                WastePercent = l.WastePercent, Name = l.Name, UnitCost = l.UnitCost, AccountCode = l.AccountCode, SortOrder = l.SortOrder,
            });
        await db.SaveChangesAsync(ct);
    }
}
