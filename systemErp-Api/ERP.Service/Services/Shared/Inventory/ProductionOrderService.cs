using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مستند تحويل وتصنيع منتج. الترحيل في معاملة واحدة:
/// 1. تُصرف كل مادة من مستودع المواد بتكلفتها الفعلية (محرك المخزون، حسب سياسة التكلفة والتشغيلات).
/// 2. الخدمات والمصاريف غير المباشرة تُحمَّل بمبالغها (لا حركة).
/// 3. يدخل المنتج التام لمستودعه بتكلفة وحدة = (المواد + الخدمات + المصاريف) ÷ الكمية.
/// 4. القيد (الجرد المستمر): مدين مخزون المنتج / دائن مخزون المواد وحسابات التكاليف المحمّلة؛ وفرق التقريب أو المعيارية
///    على حساب فروق التكلفة. في الجرد الدوري لا قيد (الأثر في بضاعة آخر المدة).
/// الإلغاء يعكس الحركات بتكلفتها المحفوظة ويعكس القيد، وفرق إعادة البناء قيد إعادة تقييم.
/// </summary>
public class ProductionOrderService : IProductionOrderService
{
    private const string SourceType = "production_order";
    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public ProductionOrderService(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting,
        INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _inventory = inventory; _posting = posting; _numbers = numbers; _tx = tx; _audit = audit;
    }

    public async Task<PagedResult<ProductionOrderDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<ProductionOrder>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(p.Status)) q = q.Where(o => o.Status == p.Status);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(o => o.OrderNumber.Contains(t) || o.ProductName.Contains(t) || o.ProductSku.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.Date).ThenByDescending(o => o.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<ProductionOrderDto>
        {
            Items = rows.Select(o => ToDto(o, includeLines: false)).ToList(), TotalCount = total,
            PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<ProductionOrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _db.Set<ProductionOrder>().AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("مستند التصنيع غير موجود.");
        var dto = ToDto(order, includeLines: true);
        if (order.Status == ProductionOrderStatuses.Draft) await FillAvailableAsync(dto.Lines, order.MaterialsWarehouseId, ct);
        return dto;
    }

    public async Task<List<ProductionOrderLineDto>> TemplateAsync(Guid productId, decimal quantity, Guid? materialsWarehouseId, CancellationToken ct = default)
    {
        var product = await ManufacturedAsync(productId, ct);
        var lines = await FromComponentsAsync(product, quantity <= 0 ? 1 : quantity, ct);
        await FillAvailableAsync(lines, materialsWarehouseId, ct);
        return lines;
    }

    public Task<ProductionOrderDto> CreateAsync(SaveProductionOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = new ProductionOrder { Id = Guid.NewGuid(), Status = ProductionOrderStatuses.Draft };
            await ApplyAsync(order, r, token);
            var date = order.Date;
            order.OrderNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = SourceType, DefaultPrefix = "MFG-", WarehouseId = order.WarehouseId ?? order.MaterialsWarehouseId, Date = date,
                ManualNumber = r.ManualNumber, UserEntered = true,
                Exists = (n, c) => _db.Set<ProductionOrder>().AnyAsync(o => o.OrderNumber == n, c),
            }, token);
            _db.Add(order);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PRODUCTION_ORDER_CREATED", nameof(ProductionOrder), order.Id.ToString(),
                $"مسودة تصنيع {order.OrderNumber}: {order.Quantity:0.####} {order.ProductName}", token);
            if (r.Post) await PostCoreAsync(order, token);
            return ToDto(order, includeLines: true);
        }, ct);

    public Task<ProductionOrderDto> UpdateAsync(Guid id, SaveProductionOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await LoadAsync(id, token);
            if (order.Status != ProductionOrderStatuses.Draft) throw new ConflictException("مستند التصنيع المرحّل لا يُعدَّل: ألغه ثم أنشئ غيره.");
            _db.RemoveRange(order.Lines.ToList());
            order.Lines.Clear();
            await ApplyAsync(order, r, token);
            await _db.SaveChangesAsync(token);
            if (r.Post) await PostCoreAsync(order, token);
            return ToDto(order, includeLines: true);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await LoadAsync(id, token);
            if (order.Status != ProductionOrderStatuses.Draft) throw new ConflictException("مستند التصنيع المرحّل لا يُحذف: يُلغى فيُعكس أثره.");
            _db.RemoveRange(order.Lines.ToList());
            _db.Remove(order);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PRODUCTION_ORDER_DELETED", nameof(ProductionOrder), order.Id.ToString(), $"حذف مسودة تصنيع {order.OrderNumber}", token);
        }, ct);

    public Task<ProductionOrderDto> PostAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await LoadAsync(id, token);
            if (order.Status != ProductionOrderStatuses.Draft) throw new ConflictException("مستند التصنيع مرحّل أو ملغى.");
            await PostCoreAsync(order, token);
            return ToDto(order, includeLines: true);
        }, ct);

    public Task<ProductionOrderDto> CancelAsync(Guid id, string reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ValidationFailedException("سبب الإلغاء مطلوب.");
            var order = await LoadAsync(id, token);
            if (order.Status != ProductionOrderStatuses.Posted) throw new ConflictException("يُلغى مستند التصنيع المرحّل فقط.");
            var note = $"إلغاء تصنيع {order.OrderNumber}: {reason.Trim()}";
            if (order.JournalEntryId is { } entryId) await _posting.ReverseAsync(entryId, note, token);

            var accounts = await PostingAccounts.SettingsAsync(_db, token);
            // المنتج التام يخرج بتكلفة دخوله، والمواد تعود بتكلفة خروجها؛ وما يختلف عن أثرها الأصلي (حركات تلتها) إعادة تقييم
            var productOut = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = order.ProductId, WarehouseId = order.WarehouseId, Quantity = order.Quantity, Type = StockMovementType.AdjustmentOut,
                OutUnitCost = order.UnitCost, ReferenceNumber = order.OrderNumber, Date = order.Date, Notes = note,
                SourceType = SourceType + "_reversal", SourceId = order.Id,
                BatchAllocations = await ItemBatches.OfDocumentAsync(_db, SourceType, order.Id, order.ProductId, null, token),
            }, token);
            var productResidual = productOut.ValueChange + order.TotalCost;
            decimal materialsResidual = 0;
            foreach (var line in order.Lines.Where(l => l.Kind == ProductionLineKinds.Material && l.ComponentProductId.HasValue && l.Quantity > 0))
            {
                var back = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ComponentProductId!.Value, WarehouseId = order.MaterialsWarehouseId, Quantity = line.Quantity,
                    Type = StockMovementType.AdjustmentIn, UnitCost = line.UnitCost, ReferenceNumber = order.OrderNumber, Date = order.Date, Notes = note,
                    SourceType = SourceType + "_reversal", SourceId = order.Id, SourceLineId = line.Id,
                    BatchAllocations = await ItemBatches.OfDocumentAsync(_db, SourceType, order.Id, line.ComponentProductId.Value, line.Id, token),
                }, token);
                materialsResidual += back.ValueChange - line.TotalCost;
            }
            await ItemBatches.MarkReversedAsync(_db, SourceType, order.Id, token);
            await _db.SaveChangesAsync(token);
            if (order.JournalEntryId != null)
            {
                await InventoryValuation.PostRevaluationAsync(_db, _posting, productResidual,
                    await accounts.InventoryAsync(_db, order.WarehouseId, token), note, order.Id, token);
                await InventoryValuation.PostRevaluationAsync(_db, _posting, materialsResidual,
                    await accounts.InventoryAsync(_db, order.MaterialsWarehouseId, token), note, order.Id, token);
            }
            order.Status = ProductionOrderStatuses.Cancelled;
            order.CancelReason = reason.Trim();
            order.JournalEntryId = null;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PRODUCTION_ORDER_CANCELLED", nameof(ProductionOrder), order.Id.ToString(), note, token);
            return ToDto(order, includeLines: true);
        }, ct);

    // ---------------- البناء ----------------
    private async Task ApplyAsync(ProductionOrder order, SaveProductionOrderDto r, CancellationToken ct)
    {
        if (r.Quantity <= 0) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        var product = await ManufacturedAsync(r.ProductId, ct);
        var materialsWarehouse = await WarehouseStocks.ResolveAsync(_db, r.MaterialsWarehouseId, ct);
        var finishedWarehouse = r.WarehouseId.HasValue ? await WarehouseStocks.ResolveAsync(_db, r.WarehouseId, ct) : materialsWarehouse;
        await CostCenterRules.EnsureUsableAsync(_db, new[] { r.CostCenterId }, ct, requireActive: true);

        order.Date = (r.Date ?? SaudiTime.Today).Date;
        order.ProductId = product.Id; order.ProductName = product.NameAr; order.ProductSku = product.Sku; order.Unit = product.Unit;
        order.Quantity = r.Quantity;
        order.MaterialsWarehouseId = materialsWarehouse.Id;
        order.WarehouseId = finishedWarehouse.Id;
        order.CostCenterId = r.CostCenterId;
        order.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        if (product.TrackBatches)
        {
            order.BatchNumber = string.IsNullOrWhiteSpace(r.BatchNumber) ? null : r.BatchNumber.Trim();
            order.ProductionDate = (r.ProductionDate ?? order.Date).Date;
            order.ExpiryDate = r.ExpiryDate?.Date ?? (product.ShelfLifeDays is { } days ? order.ProductionDate.Value.AddDays(days) : null);
        }
        else { order.BatchNumber = null; order.ProductionDate = null; order.ExpiryDate = null; }

        var lines = r.Lines ?? await FromComponentsAsync(product, r.Quantity, ct);
        await ValidateLinesAsync(product.Id, lines, ct);
        var sort = 0;
        foreach (var l in lines)
        {
            var line = new ProductionOrderLine
            {
                Id = Guid.NewGuid(), ProductionOrderId = order.Id, Kind = l.Kind, ComponentProductId = l.ComponentProductId,
                Name = l.Name, Sku = l.Sku, Unit = l.Unit, StandardQuantity = l.StandardQuantity, Quantity = l.Quantity,
                UnitCost = l.Kind == ProductionLineKinds.Material ? 0 : l.UnitCost,
                AccountCode = l.Kind == ProductionLineKinds.Material ? null : l.AccountCode, SortOrder = sort++,
            };
            line.TotalCost = line.Kind == ProductionLineKinds.Material ? 0 : DocumentPricing.Round(line.Quantity * line.UnitCost);
            order.Lines.Add(line);
            _db.Add(line);
        }
        Totals(order);
    }

    /// <summary>أسطر من مكونات المنتج للكمية المنتجة: المادة بكميتها مع الهدر، والخدمة بكميتها وتكلفتها المعيارية، والمصروف بعدد الوحدات.</summary>
    private async Task<List<ProductionOrderLineDto>> FromComponentsAsync(Product product, decimal quantity, CancellationToken ct)
    {
        var (components, _) = await ProductComponents.DescribeAsync(_db, product.Id, ct);
        if (components.Count == 0) throw new ValidationFailedException($"«{product.NameAr}» بلا مكونات: عرّف مواده في شاشة الأصناف.");
        return components.Select(c =>
        {
            if (c.Kind == ProductComponentKinds.Overhead)
                return new ProductionOrderLineDto
                {
                    Kind = ProductionLineKinds.Overhead, Name = c.Name ?? string.Empty, StandardQuantity = quantity, Quantity = quantity,
                    UnitCost = c.UnitCost, TotalCost = DocumentPricing.Round(quantity * c.UnitCost), AccountCode = c.AccountCode,
                };
            var isService = c.ComponentItemType == ProductItemTypes.Service;
            var qty = Math.Round(c.Quantity * (1 + c.WastePercent / 100m) * quantity, 4);
            return new ProductionOrderLineDto
            {
                Kind = isService ? ProductionLineKinds.Service : ProductionLineKinds.Material, ComponentProductId = c.ComponentProductId,
                Name = c.ComponentName ?? string.Empty, Sku = c.ComponentSku, Unit = c.ComponentUnit, StandardQuantity = qty, Quantity = qty,
                UnitCost = c.ComponentUnitCost, TotalCost = DocumentPricing.Round(qty * c.ComponentUnitCost),
                AccountCode = isService ? c.AccountCode : null,
            };
        }).ToList();
    }

    private async Task ValidateLinesAsync(Guid productId, List<ProductionOrderLineDto> lines, CancellationToken ct)
    {
        lines.RemoveAll(l => l.Kind == ProductionLineKinds.Overhead ? string.IsNullOrWhiteSpace(l.Name) && l.UnitCost == 0 : !l.ComponentProductId.HasValue);
        if (!lines.Any(l => l.Kind == ProductionLineKinds.Material)) throw new ValidationFailedException("مستند التصنيع يحتاج مادة أولية واحدة على الأقل.");
        var ids = lines.Where(l => l.ComponentProductId.HasValue).Select(l => l.ComponentProductId!.Value).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var accounts = lines.Where(l => !string.IsNullOrWhiteSpace(l.AccountCode)).Select(l => l.AccountCode!.Trim()).Distinct().ToList();
        var foundAccounts = await _db.Set<Account>().AsNoTracking().Where(a => accounts.Contains(a.Code)).Select(a => a.Code).ToListAsync(ct);
        foreach (var l in lines)
        {
            if (!ProductionLineKinds.IsValid(l.Kind)) throw new ValidationFailedException("نوع سطر التصنيع غير صالح.");
            if (l.Quantity < 0 || l.UnitCost < 0) throw new ValidationFailedException("الكميات والمبالغ في مستند التصنيع لا تكون سالبة.");
            l.AccountCode = string.IsNullOrWhiteSpace(l.AccountCode) ? null : l.AccountCode.Trim();
            if (l.AccountCode != null && !foundAccounts.Contains(l.AccountCode)) throw new ValidationFailedException($"الحساب {l.AccountCode} غير موجود في دليل الحسابات.");
            if (l.Kind == ProductionLineKinds.Overhead)
            {
                if (string.IsNullOrWhiteSpace(l.Name)) throw new ValidationFailedException("اسم المصروف غير المباشر مطلوب.");
                l.Name = l.Name.Trim(); l.ComponentProductId = null; l.Sku = null; l.Unit = null;
                continue;
            }
            if (l.ComponentProductId == productId) throw new ValidationFailedException("لا يدخل المنتج في تصنيع نفسه.");
            if (!products.TryGetValue(l.ComponentProductId!.Value, out var p)) throw new ValidationFailedException(Messages.ProductNotFound);
            var type = ProductItemTypes.Normalize(p.ItemType);
            if (l.Kind == ProductionLineKinds.Material && !ProductItemTypes.TracksStock(type))
                throw new ValidationFailedException($"«{p.NameAr}» ليس مادة لها رصيد: تُصرف المواد من أصناف البضاعة أو المنتجات الصناعية.");
            if (l.Kind == ProductionLineKinds.Service && type != ProductItemTypes.Service)
                throw new ValidationFailedException($"«{p.NameAr}» ليس صنفاً خدمياً.");
            l.Name = p.NameAr; l.Sku = p.Sku; l.Unit = p.Unit;
            if (l.Kind == ProductionLineKinds.Service && l.UnitCost == 0) l.UnitCost = p.StandardCost ?? 0;
        }
    }

    // ---------------- الترحيل ----------------
    private async Task PostCoreAsync(ProductionOrder order, CancellationToken ct)
    {
        var product = await ManufacturedAsync(order.ProductId, ct);
        if (order.Quantity <= 0) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        var materials = order.Lines.Where(l => l.Kind == ProductionLineKinds.Material && l.Quantity > 0).OrderBy(l => l.SortOrder).ToList();
        if (materials.Count == 0) throw new ValidationFailedException("مستند التصنيع يحتاج مادة أولية بكمية موجبة.");
        var note = $"تصنيع {order.OrderNumber} - {order.Quantity:0.####} {order.ProductName}";

        foreach (var line in materials)
        {
            var recorded = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = line.ComponentProductId!.Value, WarehouseId = order.MaterialsWarehouseId, Quantity = line.Quantity,
                Type = StockMovementType.AdjustmentOut, ReferenceNumber = order.OrderNumber, Date = order.Date,
                Notes = $"صرف مواد {note}", SourceType = SourceType, SourceId = order.Id, SourceLineId = line.Id,
            }, ct);
            line.UnitCost = recorded.UnitCost;
            line.TotalCost = -recorded.ValueChange; // قيمة ما خرج فعلاً من المخزون
        }
        foreach (var line in order.Lines.Where(l => l.Kind != ProductionLineKinds.Material))
            line.TotalCost = DocumentPricing.Round(line.Quantity * line.UnitCost);
        Totals(order);
        if (order.TotalCost < 0) throw new ValidationFailedException("تكلفة المنتج لا تكون سالبة.");

        var finished = await _inventory.RecordMovementAsync(new RecordStockMovementDto
        {
            ItemId = product.Id, WarehouseId = order.WarehouseId, Quantity = order.Quantity, Type = StockMovementType.AdjustmentIn,
            UnitCost = order.UnitCost, ReferenceNumber = order.OrderNumber, Date = order.Date, Notes = $"استلام منتج {note}",
            SourceType = SourceType, SourceId = order.Id,
            BatchNumber = order.BatchNumber, ProductionDate = order.ProductionDate, ExpiryDate = order.ExpiryDate,
        }, ct);
        if (finished.Batches is { Count: > 0 } parts && order.BatchNumber == null) order.BatchNumber = parts[0].BatchNumber;

        order.JournalEntryId = (await PostEntryAsync(order, finished.ValueChange, note, ct))?.JournalEntryId;
        order.Status = ProductionOrderStatuses.Posted;
        order.PostedAt = SaudiTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PRODUCTION_ORDER_POSTED", nameof(ProductionOrder), order.Id.ToString(),
            $"ترحيل {note} بتكلفة {order.TotalCost:0.00} (الوحدة {order.UnitCost:0.####})", ct);
    }

    /// <summary>
    /// قيد التصنيع: مدين مخزون المنتج بتغيّر قيمته الفعلي / دائن مخزون المواد بقيمة ما خرج، ودائن حساب كل خدمة ومصروف محمَّل بمبلغه.
    /// ما يبقى (تقريب، أو منتج بتكلفة معيارية) على حساب فروق التكلفة. الجرد الدوري: لا قيد.
    /// </summary>
    private async Task<PostingResult?> PostEntryAsync(ProductionOrder order, decimal productValue, string note, CancellationToken ct)
    {
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        if (accounts.IsPeriodic(order.Date)) return null;
        var productAccount = await accounts.InventoryAsync(_db, order.WarehouseId, ct);
        var materialsAccount = await accounts.InventoryAsync(_db, order.MaterialsWarehouseId, ct);
        var lines = new List<PostingLine>();
        void Add(string account, decimal amount, string text)
        {
            amount = DocumentPricing.Round(amount);
            if (amount > 0) lines.Add(new PostingLine(account, amount, 0, text, order.CostCenterId));
            else if (amount < 0) lines.Add(new PostingLine(account, 0, -amount, text, order.CostCenterId));
        }
        Add(productAccount, productValue, $"منتج تام {order.ProductName}");
        Add(materialsAccount, -order.MaterialsCost, "مواد أولية مصروفة للتصنيع");
        foreach (var group in order.Lines.Where(l => l.Kind != ProductionLineKinds.Material && l.TotalCost != 0)
                     .GroupBy(l => l.AccountCode ?? DefaultAccounts.AppliedManufacturingCosts))
            Add(group.Key, -group.Sum(l => l.TotalCost), string.Join("، ", group.Select(l => l.Name).Distinct()));
        var difference = lines.Sum(l => l.Debit - l.Credit);
        if (difference != 0)
        {
            var configured = await _db.Set<CostingPolicy>().AsNoTracking().Select(p => p.StandardCostVarianceAccountCode).FirstOrDefaultAsync(ct);
            Add(string.IsNullOrWhiteSpace(configured) ? DefaultAccounts.InventoryAdjustment : configured.Trim(), -difference, "فرق تكلفة التصنيع");
        }
        if (lines.Count == 0) return null;
        await DefaultAccounts.EnsureAsync(_db, ct, lines.Select(l => l.AccountCode).Distinct().ToArray());
        return await _posting.PostAsync(new GenericPostingRequest
        {
            Date = order.Date, Description = note, SourceType = SourceType, SourceId = order.Id, SourceNumber = order.OrderNumber, Lines = lines,
        }, ct);
    }

    // ---------------- مساعدات ----------------
    private static void Totals(ProductionOrder order)
    {
        order.MaterialsCost = order.Lines.Where(l => l.Kind == ProductionLineKinds.Material).Sum(l => l.TotalCost);
        order.ServicesCost = order.Lines.Where(l => l.Kind == ProductionLineKinds.Service).Sum(l => l.TotalCost);
        order.OverheadCost = order.Lines.Where(l => l.Kind == ProductionLineKinds.Overhead).Sum(l => l.TotalCost);
        order.TotalCost = order.MaterialsCost + order.ServicesCost + order.OverheadCost;
        order.UnitCost = order.Quantity == 0 ? 0 : Math.Round(order.TotalCost / order.Quantity, 4);
    }

    private async Task<Product> ManufacturedAsync(Guid productId, CancellationToken ct)
    {
        var product = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new NotFoundException(Messages.ProductNotFound);
        if (ProductItemTypes.Normalize(product.ItemType) != ProductItemTypes.Manufactured)
            throw new ValidationFailedException($"«{product.NameAr}» ليس منتجاً صناعياً: غيّر طبيعته في شاشة الأصناف وعرّف مكوناته.");
        return product;
    }

    private async Task<ProductionOrder> LoadAsync(Guid id, CancellationToken ct)
        => await _db.Set<ProductionOrder>().Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == id, ct)
           ?? throw new NotFoundException("مستند التصنيع غير موجود.");

    private async Task FillAvailableAsync(List<ProductionOrderLineDto> lines, Guid? warehouseId, CancellationToken ct)
    {
        var ids = lines.Where(l => l.Kind == ProductionLineKinds.Material && l.ComponentProductId.HasValue).Select(l => l.ComponentProductId!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        var warehouse = await WarehouseStocks.ResolveAsync(_db, warehouseId, ct);
        var stock = await _db.Set<WarehouseStock>().AsNoTracking().Where(s => s.WarehouseId == warehouse.Id && ids.Contains(s.ItemId))
            .ToDictionaryAsync(s => s.ItemId, s => s.Quantity, ct);
        foreach (var l in lines.Where(l => l.Kind == ProductionLineKinds.Material && l.ComponentProductId.HasValue))
            l.Available = stock.GetValueOrDefault(l.ComponentProductId!.Value);
    }

    private static ProductionOrderDto ToDto(ProductionOrder o, bool includeLines)
    {
        var dto = new ProductionOrderDto
        {
            Id = o.Id, CreatedAt = o.CreatedAt, OrderNumber = o.OrderNumber, Date = o.Date, ProductId = o.ProductId, ProductName = o.ProductName,
            ProductSku = o.ProductSku, Unit = o.Unit, Quantity = o.Quantity, MaterialsWarehouseId = o.MaterialsWarehouseId, WarehouseId = o.WarehouseId,
            Status = o.Status, MaterialsCost = o.MaterialsCost, ServicesCost = o.ServicesCost, OverheadCost = o.OverheadCost, TotalCost = o.TotalCost,
            UnitCost = o.UnitCost, CostCenterId = o.CostCenterId, JournalEntryId = o.JournalEntryId, BatchNumber = o.BatchNumber,
            ProductionDate = o.ProductionDate, ExpiryDate = o.ExpiryDate, Notes = o.Notes, PostedAt = o.PostedAt, CancelReason = o.CancelReason,
        };
        if (includeLines)
            dto.Lines = o.Lines.OrderBy(l => l.SortOrder).Select(l => new ProductionOrderLineDto
            {
                Id = l.Id, Kind = l.Kind, ComponentProductId = l.ComponentProductId, Name = l.Name, Sku = l.Sku, Unit = l.Unit,
                StandardQuantity = l.StandardQuantity, Quantity = l.Quantity, UnitCost = l.UnitCost, TotalCost = l.TotalCost,
                AccountCode = l.AccountCode, SortOrder = l.SortOrder,
            }).ToList();
        return dto;
    }
}
