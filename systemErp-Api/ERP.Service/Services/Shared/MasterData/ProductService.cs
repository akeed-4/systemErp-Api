using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class ProductService : CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>, IProductService
{
    private readonly IAccountingPostingService _posting;
    private readonly IPermissionService _permissions;
    public ProductService(ErpDbContext db, IAccountingPostingService posting, IPermissionService permissions) : base(db)
    {
        _posting = posting; _permissions = permissions;
    }
    protected override string Label => Messages.LabelProduct;
    protected override bool Transactional => true;

    protected override IQueryable<Product> ApplySearch(IQueryable<Product> q, string t)
        => q.Where(p => p.Sku.Contains(t) || p.NameAr.Contains(t) || p.NameEn.Contains(t) || (p.Barcode != null && p.Barcode.Contains(t)));

    protected override IQueryable<Product> ApplyFilters(IQueryable<Product> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Category == p.Status); // Status = رمز التصنيف في شاشة الأصناف

    /// <summary>طول المفتاح المفهرس BarcodeLookupKey (LEFT([Barcode], 450)).</summary>
    private const int BarcodeLookupKeyLength = 450;

    /// <summary>
    /// الباركود مستخدم لصنف آخر: المطابقة بالقيمة الأصلية كما هي، ويُضاف المفتاح المفهرس حين يساوي الباركود كاملاً
    /// (حتى 450 حرفاً) ليُبحث بالفهرس بدل مسح الجدول؛ الأطول يُقارن بالقيمة الأصلية وحدها كما كان.
    /// </summary>
    private Task<bool> BarcodeInUseAsync(string barcode, Guid? exceptId, CancellationToken ct)
        => barcode.Length <= BarcodeLookupKeyLength
            ? Db.Set<Product>().AnyAsync(p => p.BarcodeLookupKey == barcode && p.Barcode == barcode && (exceptId == null || p.Id != exceptId), ct)
            : Db.Set<Product>().AnyAsync(p => p.Barcode == barcode && (exceptId == null || p.Id != exceptId), ct);

    protected override async Task ValidateAsync(CreateProductDto d, Product? existing, CancellationToken ct)
    {
        var errors = FieldErrors(d);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Product>().AnyAsync(p => p.Sku == d.Sku && (existing == null || p.Id != existing.Id), ct))
            throw new ConflictException(Messages.SkuInUse);
        if (!string.IsNullOrWhiteSpace(d.Barcode) && await BarcodeInUseAsync(d.Barcode, existing?.Id, ct))
            throw new ConflictException(Messages.BarcodeInUse);
        if (!string.IsNullOrWhiteSpace(d.Category) && !await Db.Set<ProductCategory>().AnyAsync(c => c.Code == d.Category, ct))
            throw new ValidationFailedException(Messages.ProductCategoryNotFound);
        if (!string.IsNullOrWhiteSpace(d.Unit) && !await Db.Set<UnitOfMeasure>().AnyAsync(u => u.Code == d.Unit, ct))
            throw new ValidationFailedException(Messages.UnitOfMeasureNotFound);
        await ValidateItemTypeAsync(d, existing, ct);
    }

    /// <summary>المكونات المتحقق منها لهذا الحفظ (تُكتب بعد حفظ الصنف)؛ null = لا تغيير على مكوناته.</summary>
    private List<ProductComponentDto>? _pendingComponents;

    /// <summary>
    /// طبيعة الصنف ومكوناته: الخدمي والمجمّع والمركّب بلا رصيد ولا تتبّع تشغيلات؛ تحويل صنف له حركات مخزنية إلى طبيعة بلا رصيد ممنوع
    /// (يبقى رصيده وقيمته بلا صنف يحملهما)، وتحويل صنف له مكونات إلى طبيعة بلا مكونات يحذفها.
    /// </summary>
    private async Task ValidateItemTypeAsync(CreateProductDto d, Product? existing, CancellationToken ct)
    {
        var type = ProductItemTypes.Normalize(d.ItemType);
        if (!ProductItemTypes.IsValid(type)) throw new ValidationFailedException("طبيعة الصنف غير صالحة.");
        d.ItemType = type;
        if (!ProductItemTypes.TracksStock(type))
        {
            d.TrackBatches = false; d.TrackExpiry = false; d.MinStockLevel = 0;
            if (existing != null && ProductItemTypes.TracksStock(existing.ItemType)
                && (existing.CurrentStock != 0 || await Db.Set<StockMovement>().AnyAsync(m => m.ItemId == existing.Id, ct)))
                throw new ConflictException($"«{existing.NameAr}» له حركات مخزنية: لا يتحول إلى صنف بلا رصيد (خدمي أو مجمّع أو مركّب).");
            if (existing != null && ProductItemTypes.TracksStock(existing.ItemType)
                && await Db.Set<ProductComponent>().AnyAsync(c => c.ComponentProductId == existing.Id, ct)
                && type != ProductItemTypes.Service)
                throw new ConflictException($"«{existing.NameAr}» مكوّن في صنف آخر: لا يتحول إلى صنف مجمّع أو مركّب.");
        }
        if (type == ProductItemTypes.Service && existing != null
            && await Db.Set<ProductComponent>().AnyAsync(c => c.ComponentProductId == existing.Id && c.ProductId != existing.Id
                && Db.Set<Product>().Any(p => p.Id == c.ProductId && (p.ItemType == ProductItemTypes.Kit || p.ItemType == ProductItemTypes.Composite)), ct))
            throw new ConflictException($"«{existing.NameAr}» مكوّن في صنف مجمّع أو مركّب: لا يتحول إلى خدمي.");

        if (d.Components != null)
            _pendingComponents = await ProductComponents.ValidateAsync(Db, existing?.Id, type, d.Components, ct);
        else if (!ProductItemTypes.HasComponents(type))
            _pendingComponents = new(); // طبيعة بلا مكونات: تُحذف مكوناته السابقة إن وُجدت
        else if (existing == null || !ProductItemTypes.HasComponents(existing.ItemType)
                 || (ProductItemTypes.SellsComponents(type) != ProductItemTypes.SellsComponents(existing.ItemType)))
        {
            // صنف جديد (أو تغيّرت طبيعته) بلا مكونات مرسلة: تُتحقق مكوناته الحالية بطبيعته الجديدة
            var current = existing == null ? new List<ProductComponentDto>() : (await ProductComponents.DescribeAsync(Db, existing.Id, ct)).Lines;
            _pendingComponents = await ProductComponents.ValidateAsync(Db, existing?.Id, type, current, ct);
        }
    }

    public override async Task<ProductDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await base.GetAsync(id, ct);
        dto.ItemType = ProductItemTypes.Normalize(dto.ItemType);
        if (ProductItemTypes.HasComponents(dto.ItemType))
        {
            var (lines, cost) = await ProductComponents.DescribeAsync(Db, id, ct);
            dto.Components = lines;
            dto.EstimatedComponentsCost = cost;
        }
        else dto.Components = new();
        return dto;
    }

    private async Task WritePendingComponentsAsync(Guid productId, CancellationToken ct)
    {
        if (_pendingComponents == null) return;
        var lines = _pendingComponents;
        _pendingComponents = null;
        await ProductComponents.ReplaceAsync(Db, productId, lines, ct);
    }

    /// <summary>قواعد حقول الصنف التي لا تحتاج قاعدة البيانات (مشتركة بين الحفظ المفرد والاستيراد الجماعي).</summary>
    private static List<string> FieldErrors(CreateProductDto d)
    {
        var errors = new List<string>();
        d.ItemType = ProductItemTypes.Normalize(d.ItemType);
        if (!ProductItemTypes.IsValid(d.ItemType)) errors.Add("طبيعة الصنف غير صالحة.");
        if (string.IsNullOrWhiteSpace(d.Sku)) errors.Add(Messages.SkuRequired);
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ProductArabicNameRequired);
        if (d.SellingPrice < 0 || d.StandardCost < 0 || d.MinStockLevel < 0 || d.WeightKg < 0) errors.Add(Messages.PricesAndReorderCannotBeNegative);
        if (d.VatRate is < 0 or > 100) errors.Add(Messages.VatRateRange);
        if (d.TrackExpiry && !d.TrackBatches) errors.Add(Messages.ExpiryTrackingRequiresBatches);
        if (d.ShelfLifeDays is < 0 || d.ExpiryAlertDays < 0) errors.Add(Messages.PricesAndReorderCannotBeNegative);
        if (d.ExciseTaxRate is < 0 or > 100 || d.ExciseRetailPrice < 0 || d.ExciseStandardPrice < 0)
            errors.Add("نسبة الضريبة الانتقائية بين 0 و100 والأسعار المرجعية لا تكون سالبة.");
        if (d.ExciseTaxRate > 0 && d.ExciseRetailPrice <= 0 && d.ExciseStandardPrice <= 0)
            errors.Add("الصنف الخاضع للضريبة الانتقائية يحتاج سعر تجزئة محدداً أو سعراً قياسياً معتمداً.");
        if (!Enum.IsDefined(d.VatCategory)) errors.Add("التصنيف الضريبي غير صالح.");
        else if (d.VatCategory == VatCategory.MarginScheme) errors.Add(Messages.ProductMarginSchemeNotAllowed); // من بطاقة السيارة وحدها
        else if (d.VatCategory == VatCategory.Standard) d.VatExemptionReasonCode = null;
        else
        {
            // صنف صفري أو معفى أو خارج النطاق: بلا ضريبة، وسببه برمز الهيئة يطابق تصنيفه (يحمله كل سطر يبيع به)
            var reason = ERP.Service.Services.Accounting.VatExemptionReasons.Find(d.VatExemptionReasonCode);
            if (reason == null) errors.Add("سبب الصفرية أو الإعفاء مطلوب للصنف غير الخاضع للضريبة.");
            else if (reason.Category != d.VatCategory) errors.Add("سبب الإعفاء لا يطابق التصنيف الضريبي للصنف.");
            else d.VatExemptionReasonCode = reason.Code;
            d.VatRate = 0;
        }
        return errors;
    }

    private const int ImportSaveBatch = 1000;

    /// <summary>
    /// استيراد جماعي سريع لملفات كبيرة (مئات آلاف الأصناف): الرموز والباركودات والتصنيفات والوحدات تُحمَّل مرة واحدة،
    /// والحفظ على دفعات، وعدّاد التصنيفات يُعاد حسابه مرة واحدة في النهاية بدل كل صف.
    /// - التصنيف يُقبل برمزه أو باسمه، وإن لم يوجد يُنشأ تلقائياً باسمه كما في الملف.
    /// - الصف بلا رمز صنف يأخذ رمزاً فريداً تلقائياً (SKU-n).
    /// صف فاشل لا يوقف البقية، ورقم الصف = ترتيبه في الدفعة + 2 (كما في CrudService).
    /// </summary>
    public override async Task<ImportResultDto> ImportAsync(List<CreateProductDto> items, CancellationToken ct = default)
    {
        var result = new ImportResultDto { TotalRows = items.Count };
        if (items.Count == 0) return result;

        var skus = new HashSet<string>(await Db.Set<Product>().AsNoTracking().Select(p => p.Sku).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);
        var barcodes = new HashSet<string>(
            await Db.Set<Product>().AsNoTracking().Where(p => p.Barcode != null && p.Barcode != "").Select(p => p.Barcode!).ToListAsync(ct),
            StringComparer.OrdinalIgnoreCase);
        var units = new HashSet<string>(await Db.Set<UnitOfMeasure>().AsNoTracking().Select(u => u.Code).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);
        var categoryByKey = await ResolveImportCategoriesAsync(items, ct);
        var nextSku = 1001;

        var autoDetect = Db.ChangeTracker.AutoDetectChangesEnabled;
        Db.ChangeTracker.AutoDetectChangesEnabled = false;
        var pending = new List<(Product Entity, ImportRowResult Row)>();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var d = items[i];
                var row = new ImportRowResult { RowNumber = i + 2 };
                result.Results.Add(row);

                d.Sku = d.Sku?.Trim() ?? string.Empty;
                d.Barcode = string.IsNullOrWhiteSpace(d.Barcode) ? null : d.Barcode.Trim();
                d.NameAr = d.NameAr?.Trim() ?? string.Empty;
                d.NameEn = d.NameEn?.Trim() ?? string.Empty;
                d.Unit = d.Unit?.Trim() ?? string.Empty;
                if (d.Sku.Length == 0)
                {
                    while (skus.Contains($"SKU-{nextSku}")) nextSku++;
                    d.Sku = $"SKU-{nextSku++}";
                }
                d.Category = categoryByKey.TryGetValue(CategoryKey(d.Category), out var code) ? code : string.Empty;

                var error = FieldErrors(d).FirstOrDefault();
                // الاستيراد للأصناف بلا مكونات: المجمّع والمركّب والمنتج الصناعي تُعرَّف مكوناتها من شاشة الصنف
                if (error == null && ProductItemTypes.HasComponents(d.ItemType))
                    error = "الصنف المجمّع أو المركّب أو المنتج الصناعي يُعرَّف من شاشة الصنف بمكوناته، لا بالاستيراد.";
                if (error == null && d.ItemType == ProductItemTypes.Service) { d.TrackBatches = false; d.TrackExpiry = false; }
                if (error == null && skus.Contains(d.Sku)) error = Messages.SkuInUse;
                if (error == null && d.Barcode != null && barcodes.Contains(d.Barcode)) error = Messages.BarcodeInUse;
                if (error == null && d.Unit.Length > 0 && !units.Contains(d.Unit)) error = Messages.UnitOfMeasureNotFound;
                if (error != null)
                {
                    row.Error = error;
                    continue;
                }

                skus.Add(d.Sku);
                if (d.Barcode != null) barcodes.Add(d.Barcode);
                var entity = Mapper.Map<Product>(d);
                if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
                await OnCreatingAsync(entity, d, ct);
                Db.Set<Product>().Add(entity);
                pending.Add((entity, row));

                if (pending.Count >= ImportSaveBatch) await SaveImportBatchAsync(pending, ct);
            }
            await SaveImportBatchAsync(pending, ct);
        }
        finally
        {
            Db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }

        result.SuccessCount = result.Results.Count(r => r.Success);
        result.FailedCount = result.TotalRows - result.SuccessCount;
        await RecountAsync(ct);
        return result;
    }

    /// <summary>يحفظ دفعة الاستيراد؛ فشلها (قيد فريد مثلاً) يُسجَّل على صفوفها فقط ويستمر الاستيراد.</summary>
    private async Task SaveImportBatchAsync(List<(Product Entity, ImportRowResult Row)> pending, CancellationToken ct)
    {
        if (pending.Count == 0) return;
        try
        {
            await SaveAsync(ct);
            foreach (var (entity, row) in pending) { row.Success = true; row.Id = entity.Id; }
        }
        catch (Exception ex) when (ex is ConflictException or DbUpdateException)
        {
            var message = ex is ConflictException ? ex.Message : $"خطأ غير متوقع أثناء حفظ {Label}.";
            foreach (var (_, row) in pending) row.Error = message;
        }
        finally
        {
            pending.Clear();
            Db.ChangeTracker.Clear(); // لا تتراكم آلاف الكيانات المتتبَّعة بين الدفعات
        }
    }

    /// <summary>
    /// يربط قيمة التصنيف في كل صف (رمز أو اسم) برمز تصنيف قائم، وينشئ تصنيفاً جديداً لكل اسم غير معرّف.
    /// المفتاح موحّد (بلا مسافات/تشكيل، وتوحيد الألف والتاء المربوطة والياء) فلا يتكرر التصنيف بفروق كتابة بسيطة.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveImportCategoriesAsync(List<CreateProductDto> items, CancellationToken ct)
    {
        var existing = await Db.Set<ProductCategory>().AsNoTracking().ToListAsync(ct);
        var map = new Dictionary<string, string>();
        foreach (var c in existing) map.TryAdd(CategoryKey(c.Code), c.Code);
        foreach (var c in existing) map.TryAdd(CategoryKey(c.NameAr), c.Code);
        foreach (var c in existing.Where(c => !string.IsNullOrWhiteSpace(c.NameEn))) map.TryAdd(CategoryKey(c.NameEn), c.Code);

        var codes = new HashSet<string>(existing.Select(c => c.Code), StringComparer.OrdinalIgnoreCase);
        var next = existing
            .Select(c => System.Text.RegularExpressions.Regex.Match(c.Code, @"^CAT-(\d+)$"))
            .Where(m => m.Success)
            .Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max() + 1;

        var created = 0;
        foreach (var raw in items.Select(i => i.Category))
        {
            var key = CategoryKey(raw);
            if (key.Length == 0 || map.ContainsKey(key)) continue;
            string code;
            do code = $"CAT-{next++:000}"; while (codes.Contains(code));
            codes.Add(code);
            var name = System.Text.RegularExpressions.Regex.Replace(raw!.Trim(), @"\s+", " ");
            Db.Set<ProductCategory>().Add(new ProductCategory { Id = Guid.NewGuid(), Code = code, NameAr = name, NameEn = name });
            map[key] = code;
            created++;
        }
        if (created > 0)
        {
            await SaveAsync(ct);
            Db.ChangeTracker.Clear();
        }
        return map;
    }

    private static string CategoryKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch) || ch is '_' or '-' or '.' or ':' or '*' or '(' or ')' or '#' or '/' or '\\' or '[' or ']') continue;
            if (ch is >= 'ً' and <= 'ْ' or 'ـ') continue; // تشكيل وتطويل
            sb.Append(ch switch { 'أ' or 'إ' or 'آ' => 'ا', 'ة' => 'ه', 'ى' => 'ي', _ => ch });
        }
        return sb.ToString();
    }

    protected override Task OnCreatingAsync(Product e, CreateProductDto d, CancellationToken ct)
    {
        // الرصيد والتكلفة تتغيّر بحركات المخزون فقط؛ الرصيد الافتتاحي يدخل عبر حركة تسوية.
        e.CurrentStock = 0; e.AverageCost = 0; e.LastPurchaseCost = 0;
        return Task.CompletedTask;
    }

    protected override async Task OnUpdatingAsync(Product e, UpdateProductDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.CurrentStock = o.GetValue<decimal>(nameof(Product.CurrentStock));
        e.AverageCost = o.GetValue<decimal>(nameof(Product.AverageCost));
        e.LastPurchaseCost = o.GetValue<decimal>(nameof(Product.LastPurchaseCost));

        await ApplyBatchTrackingChangeAsync(e, o.GetValue<bool>(nameof(Product.TrackBatches)), ct);

        // التكلفة المعيارية لصنف دخل حركة أو مستنداً لا يغيّرها إلا صاحب صلاحية إعادة التقييم
        if (e.StandardCost != o.GetValue<decimal?>(nameof(Product.StandardCost)) && await IsUsedAsync(e.Id, ct)
            && !await _permissions.HasPermissionAsync(PermissionService.ItemStandardCostScreenId, ScreenAction.Edit, ct))
            throw new ForbiddenException(string.Format(Messages.StandardCostLockedForUsedItem, e.NameAr));

        // بالتكلفة المعيارية: تغيير المعيارية يعيد تسعير الرصيد القائم بها فوراً، وفرق قيمته قيد إعادة تقييم
        if (e.StandardCost is not { } standard || standard == e.AverageCost || !ProductItemTypes.TracksStock(e.ItemType)) return;
        var method = await Db.Set<CostingPolicy>().AsNoTracking().Select(p => (CostingMethod?)p.Method).FirstOrDefaultAsync(ct);
        if (method != CostingMethod.Standard) return;
        var before = InventoryValuation.Value(e);
        e.AverageCost = standard;
        var description = $"إعادة تقييم {e.NameAr} بالتكلفة المعيارية {standard:0.####}";
        InventoryValuation.AddRevaluation(Db, e, InventoryValuation.Value(e) - before, "STD-COST", description, e.Id);
        await InventoryValuation.PostRevaluationAsync(Db, _posting, new[] { (e.Id, InventoryValuation.Value(e) - before) }, description, e.Id, ct);
    }

    protected override async Task OnCreatedAsync(Product e, CancellationToken ct)
    {
        await WritePendingComponentsAsync(e.Id, ct);
        await RecountAsync(ct);
    }

    protected override Task OnUpdatedAsync(Product e, CancellationToken ct) => WritePendingComponentsAsync(e.Id, ct);

    /// <summary>
    /// تفعيل تتبّع التشغيلات لصنف برصيد قائم: رصيد كل مستودع يدخل تشغيلة «رصيد افتتاحي» بلا صلاحية (فيبقى مجموع التشغيلات = الرصيد)،
    /// ثم تُستهلك أولاً بعد التشغيلات المؤرخة. الإيقاف ممنوع وفي التشغيلات رصيد (تبقى الأرصدة بلا تتبّع ثم تنفصل عن الرصيد).
    /// </summary>
    private async Task ApplyBatchTrackingChangeAsync(Product e, bool wasTracking, CancellationToken ct)
    {
        if (!e.TrackBatches) e.TrackExpiry = false;
        if (wasTracking == e.TrackBatches) return;
        if (!e.TrackBatches)
        {
            if (await Db.Set<BatchStock>().AnyAsync(s => s.ItemId == e.Id && s.Quantity != 0, ct))
                throw new ConflictException(string.Format(Messages.BatchTrackingCannotBeDisabled, e.NameAr));
            return;
        }
        var balances = await Db.Set<WarehouseStock>().Where(s => s.ItemId == e.Id && s.Quantity != 0).ToListAsync(ct);
        if (balances.Count == 0) return;
        var opening = await ItemBatches.OpeningAsync(Db, e.Id, ct);
        foreach (var b in balances)
        {
            var already = await Db.Set<BatchStock>().Where(s => s.ItemId == e.Id && s.WarehouseId == b.WarehouseId).SumAsync(s => s.Quantity, ct);
            if (b.Quantity - already != 0) await ItemBatches.ApplyAsync(Db, opening, b.WarehouseId, b.Quantity - already, ct);
        }
    }

    protected override async Task OnDeletingAsync(Product e, CancellationToken ct)
    {
        if (await Db.Set<StockMovement>().AnyAsync(m => m.ItemId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteProductWithMovements);
        if (await Db.Set<InvoiceItemBase>().AnyAsync(i => i.ItemId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteProductUsedInInvoices);
        var owner = await Db.Set<ProductComponent>().Where(c => c.ComponentProductId == e.Id)
            .Join(Db.Set<Product>(), c => c.ProductId, p => p.Id, (c, p) => p.NameAr).FirstOrDefaultAsync(ct);
        if (owner != null) throw new ConflictException($"«{e.NameAr}» مكوّن في الصنف «{owner}»: احذفه من مكوناته أولاً.");
        if (await Db.Set<ProductionOrder>().AnyAsync(o => o.ProductId == e.Id, ct)
            || await Db.Set<ProductionOrderLine>().AnyAsync(l => l.ComponentProductId == e.Id, ct))
            throw new ConflictException($"«{e.NameAr}» مستخدم في مستندات تصنيع ولا يُحذف.");
        Db.RemoveRange(await Db.Set<ProductComponent>().Where(c => c.ProductId == e.Id).ToListAsync(ct));
        // بند مكتبة المقاولات المرتبط به يبقى بلا صنف خدمي
        foreach (var w in await Db.Set<WorkItem>().Where(w => w.ProductId == e.Id).ToListAsync(ct)) w.ProductId = null;
    }

    /// <summary>الصنف «مستخدم» إن دخل حركة مخزنية أو فاتورة (أو مرتجعها) أو نقطة بيع أو سند تسليم أو جرداً.</summary>
    public async Task<bool> IsUsedAsync(Guid id, CancellationToken ct = default)
        => await Db.Set<StockMovement>().AnyAsync(m => m.ItemId == id, ct)
            || await Db.Set<InvoiceItemBase>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<PosTransactionItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<PosSalesReturnItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<DeliveryNoteItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<DeliveryReturnItem>().AnyAsync(i => i.ItemId == id, ct)
            || await Db.Set<InventoryCountLine>().AnyAsync(l => l.ItemId == id, ct);

    public override async Task<ProductDto> UpdateAsync(Guid id, UpdateProductDto dto, CancellationToken ct = default)
    {
        var result = await base.UpdateAsync(id, dto, ct);
        await RecountAsync(ct);
        return result;
    }

    public override async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await base.DeleteAsync(id, ct);
        await RecountAsync(ct);
    }

    private async Task RecountAsync(CancellationToken ct)
    {
        var counts = await Db.Set<Product>().GroupBy(p => p.Category).Select(g => new { Code = g.Key, N = g.Count() }).ToListAsync(ct);
        foreach (var c in await Db.Set<ProductCategory>().ToListAsync(ct))
            c.ItemCount = counts.FirstOrDefault(x => x.Code == c.Code)?.N ?? 0;
        await Db.SaveChangesAsync(ct);
    }
}
