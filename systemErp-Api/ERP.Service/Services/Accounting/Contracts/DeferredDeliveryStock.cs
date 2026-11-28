using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// صرف بيانات تسليم فاتورة البيع «غير المسلّمة» (<see cref="SalesInvoice.DeliveryDeferred"/>): الفاتورة رحّلت الإيراد والضريبة بلا مخزون،
/// وكل بيان منها يُخرج أصنافه من موقع صرف سطره ويُرحِّل تكلفتها (مدين تكلفة مبيعات الموقع / دائن مخزونه).
/// <para>المزامنة بالفرق لا بالتكرار: الكمية المطلوبة لكل سطر = (المؤكد إن أُكّد، وإلا كمية البيان) − مرتجع التسليم، وصفر للبيان الملغى أو المحذوف؛
/// ويُقارن بما خرج فعلاً (<see cref="DeliveryNoteItem.StockIssuedQty"/>): الزيادة حركة صرف، والنقص حركة إرجاع بتكلفة ما خرج.
/// فاستدعاؤها بعد الإنشاء أو التعديل أو التأكيد أو الإلغاء أو قبل الحذف آمن ولا يخصم مرتين.</para>
/// الفاتورة «المسلّمة» لا تمر هنا: بياناتها تشغيلية بلا حركة ولا قيد كما كانت.
/// <para>وبالطريقة نفسها بيان الاستلام من فاتورة شراء «غير مستلمة» (<see cref="PurchaseInvoice.DeliveryDeferred"/>): يُدخل المقبول
/// (الكمية − مرتجع السند) لموقع كل سطر بتكلفة سطر الفاتورة، وينقل قيمته من «بضاعة مشتراة لم تُستلم» إلى مخزون الموقع.</para>
/// </summary>
internal static class DeferredDeliveryStock
{
    public const string SourceType = "delivery_note";
    public const string ReversalSourceType = "delivery_note_reversal";
    private const decimal Tolerance = 0.00005m;

    /// <param name="removing">البيان سيُحذف: كل ما خرج (أو دخل) يُعكس.</param>
    public static async Task SyncAsync(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting, DeliveryNote note, bool removing,
        CancellationToken ct)
    {
        if (note.SourceType == DeliveryNoteSources.PurchaseInvoice)
        {
            await SyncReceiptAsync(db, inventory, posting, note, removing, ct);
            return;
        }
        if (note.SourceType != DeliveryNoteSources.SalesInvoice || note.SourceId is not Guid invoiceId) return;
        var invoice = await db.Set<SalesInvoice>().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null) return;
        // الفاتورة المسلّمة: لا صرف من البيان (إلا عكس ما خرج سابقاً إن وُجد)
        if (!invoice.DeliveryDeferred && note.Items.All(i => i.StockIssuedQty == 0)) return;

        var lines = invoice.Items.ToDictionary(l => l.Id);
        var active = !removing && invoice.DeliveryDeferred
            && note.Status is not (DeliveryNoteStatus.Cancelled or DeliveryNoteStatus.Draft)
            && note.DeliveryStatus != DeliveryTrackingStatuses.Cancelled;
        var text = $"بيان التسليم {note.DeliveryNumber} - فاتورة {invoice.InvoiceNumber}";
        var costs = new List<(Guid? Site, Guid? Center, decimal Value)>();

        // أسطر سيعود منها شيء للمخزون: لا يُسمح إن كان مرتجع الفاتورة قد أعاد جزءاً مما سُلّم (لا إرجاع مكرر)
        var decreasing = note.Items.Where(i => i.SourceLineId is Guid id && lines.ContainsKey(id) && i.StockIssuedQty > 0
                && Math.Round((active ? Math.Max(0, (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty) : 0) * InvoiceLineStock.Factor(lines[id]), 4)
                    < i.StockIssuedQty - Tolerance)
            .Select(i => i.SourceLineId!.Value).ToList();
        await EnsureNoReturnedStockAsync(db, invoice, decreasing, ct);

        foreach (var item in note.Items)
        {
            if (item.SourceLineId is not Guid lineId || !lines.TryGetValue(lineId, out var line) || !InvoiceLineStock.MovesItself(line))
                continue;
            var factor = InvoiceLineStock.Factor(line);
            var qty = active ? Math.Max(0, (item.ConfirmedQty ?? item.DeliveredQty) - item.ReturnedQty) : 0;
            var target = Math.Round(qty * factor, 4);
            var delta = target - item.StockIssuedQty;
            if (Math.Abs(delta) < Tolerance) continue;
            // موقع الصرف المحدد لسطر الفاتورة (نسخه البيان عند الإنشاء)، وإلا موقع الفاتورة
            var site = item.WarehouseId ?? line.WarehouseId ?? invoice.WarehouseId;
            var center = line.CostCenterId ?? invoice.CostCenterId;

            if (delta > 0)
            {
                // كالبيع: لا يستهلك المحجوز للمشاريع، والرصيد غير الكافي ترفضه سياسة المخزون السالب في محرك المخزون
                if (site is Guid siteId) await StockReservations.EnsureUnreservedAsync(db, line.ItemId, line.ItemName, siteId, delta, ct);
                var recorded = await inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ItemId, WarehouseId = site, Quantity = delta, Type = StockMovementType.OutSales,
                    UnitPrice = Math.Round(VatPricing.NetUnitPrice(invoice.PricingMethod, line.UnitPrice, line.VatRate) / factor, 4),
                    ItemBatchId = line.ItemBatchId, ReferenceNumber = note.DeliveryNumber, Date = note.Date, Notes = text,
                    SourceType = SourceType, SourceId = note.Id, SourceLineId = item.Id,
                }, ct);
                var value = -recorded.ValueChange;
                item.StockIssuedQty += delta;
                item.StockIssuedValue += value;
                costs.Add((site, center, value));
            }
            else
            {
                var back = -delta;
                var unitCost = item.StockIssuedQty <= 0 ? 0 : Math.Round(item.StockIssuedValue / item.StockIssuedQty, 4);
                var recorded = await inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ItemId, WarehouseId = site, Quantity = back, Type = StockMovementType.AdjustmentIn, UnitCost = unitCost,
                    ReferenceNumber = note.DeliveryNumber, Date = note.Date, Notes = $"إرجاع {text}",
                    SourceType = ReversalSourceType, SourceId = note.Id, SourceLineId = item.Id,
                    // يعود لتشغيلات ما خرج بالبيان نفسه (بعد خصم ما أُرجع منها سابقاً)
                    BatchAllocations = await RemainingBatchesAsync(db, note.Id, line.ItemId, item.Id, back, ct),
                }, ct);
                var value = recorded.ValueChange;
                var full = target <= Tolerance;
                item.StockIssuedQty = full ? 0 : item.StockIssuedQty - back;
                item.StockIssuedValue = full ? 0 : item.StockIssuedValue - value;
                costs.Add((site, center, -value));
            }
        }
        if (costs.Count == 0) return;
        // عكس كل ما خرج: توزيعات الصرف وإرجاعاته لا تُحسب بعدها (صرف لاحق بعد التعديل يبدأ تشغيلاته من جديد)
        if (!active)
        {
            await ItemBatches.MarkReversedAsync(db, SourceType, note.Id, ct);
            await ItemBatches.MarkReversedAsync(db, ReversalSourceType, note.Id, ct);
        }

        // تكلفة الفاتورة وربحها يتبعان ما خرج فعلاً ببياناتها
        var net = costs.Sum(c => c.Value);
        invoice.TotalCost += net;
        invoice.GrossProfit -= net;
        await db.SaveChangesAsync(ct);
        await PostCostAsync(db, posting, invoice, note, costs, text, ct);
    }

    /// <summary>
    /// بيان استلام فاتورة شراء «غير مستلمة»: المطلوب لكل سطر = المقبول − مرتجع السند (صفر للملغى أو المحذوف)، مقارناً بما دخل فعلاً.
    /// الزيادة حركة شراء بتكلفة سطر الفاتورة (صافيه بعد الخصم ÷ كميته الأساسية)، والنقص حركة إخراج بتكلفة ما دخل وعلى تشغيلاته.
    /// القيد: مدين مخزون الموقع بتغيّر قيمته / دائن «بضاعة مشتراة لم تُستلم» بقيمة الفاتورة، والفرق (المعيارية) على حساب الانحرافات.
    /// الجرد الدوري: مدين المشتريات / دائن 1145 بقيمة الفاتورة.
    /// </summary>
    private static async Task SyncReceiptAsync(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting, DeliveryNote note,
        bool removing, CancellationToken ct)
    {
        if (note.SourceId is not Guid invoiceId) return;
        var invoice = await db.Set<PurchaseInvoice>().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
        if (invoice == null) return;
        if (!invoice.DeliveryDeferred && note.Items.All(i => i.StockIssuedQty == 0)) return;

        var lines = invoice.Items.ToDictionary(l => l.Id);
        var active = !removing && invoice.DeliveryDeferred && note.Status is not (DeliveryNoteStatus.Cancelled or DeliveryNoteStatus.Draft);
        var fx = invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate;
        var text = $"بيان الاستلام {note.DeliveryNumber} - فاتورة {invoice.InvoiceNumber}";
        var values = new List<(Guid? Site, Guid? Center, decimal Inventory, decimal Clearing)>();

        foreach (var item in note.Items)
        {
            if (item.SourceLineId is not Guid lineId || !lines.TryGetValue(lineId, out var line) || !InvoiceLineStock.MovesItself(line)) continue;
            var factor = InvoiceLineStock.Factor(line);
            var target = active ? Math.Round(Math.Max(0, item.DeliveredQty - item.ReturnedQty) * factor, 4) : 0;
            var delta = target - item.StockIssuedQty;
            if (Math.Abs(delta) < Tolerance) continue;
            var site = item.WarehouseId ?? line.WarehouseId ?? invoice.WarehouseId;
            var center = line.CostCenterId ?? invoice.CostCenterId;
            var lineBase = InvoiceLineStock.BaseQuantity(line);

            if (delta > 0)
            {
                // تكلفة الوحدة الأساسية كفاتورة الشراء: صافي السطر بالعملة الأساسية ÷ كميته الأساسية
                var unitCost = lineBase == 0 ? 0 : Math.Round(DocumentPricing.Round(line.TotalBeforeVat * fx) / lineBase, 4);
                var recorded = await inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ItemId, WarehouseId = site, Quantity = delta, Type = StockMovementType.InPurchase, UnitCost = unitCost,
                    UnitPrice = Math.Round(VatPricing.NetUnitPrice(invoice.PricingMethod, line.UnitPrice, line.VatRate) / factor, 4),
                    BatchNumber = line.BatchNumber, ProductionDate = line.ProductionDate, ExpiryDate = line.ExpiryDate,
                    ReferenceNumber = note.DeliveryNumber, Date = note.Date, Notes = text,
                    SourceType = SourceType, SourceId = note.Id, SourceLineId = item.Id,
                }, ct);
                var clearing = DocumentPricing.Round(unitCost * delta);
                item.StockIssuedQty += delta;
                item.StockIssuedValue += clearing;
                values.Add((site, center, recorded.ValueChange, clearing));
            }
            else
            {
                var back = -delta;
                var unitCost = item.StockIssuedQty <= 0 ? 0 : Math.Round(item.StockIssuedValue / item.StockIssuedQty, 4);
                var full = target <= Tolerance;
                var clearing = full ? item.StockIssuedValue : DocumentPricing.Round(unitCost * back);
                var recorded = await inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ItemId, WarehouseId = site, Quantity = back, Type = StockMovementType.AdjustmentOut, OutUnitCost = unitCost,
                    ReferenceNumber = note.DeliveryNumber, Date = note.Date, Notes = $"إلغاء {text}",
                    SourceType = ReversalSourceType, SourceId = note.Id, SourceLineId = item.Id,
                    BatchAllocations = await RemainingBatchesAsync(db, note.Id, line.ItemId, item.Id, back, ct),
                }, ct);
                item.StockIssuedQty = full ? 0 : item.StockIssuedQty - back;
                item.StockIssuedValue = full ? 0 : item.StockIssuedValue - clearing;
                values.Add((site, center, recorded.ValueChange, -clearing));
            }
        }
        if (values.Count == 0) return;
        if (!active)
        {
            await ItemBatches.MarkReversedAsync(db, SourceType, note.Id, ct);
            await ItemBatches.MarkReversedAsync(db, ReversalSourceType, note.Id, ct);
        }
        await db.SaveChangesAsync(ct);

        var settings = await PostingAccounts.SettingsAsync(db, ct);
        var periodic = settings.IsPeriodic(note.Date);
        string? variance = null;
        var amounts = new Dictionary<(string Account, Guid? Center), decimal>(); // موجب مدين، سالب دائن
        void Add(string account, Guid? center, decimal amount)
        {
            if (amount == 0) return;
            amounts[(account, center)] = amounts.GetValueOrDefault((account, center)) + amount;
        }
        foreach (var g in values.GroupBy(v => (v.Site, v.Center)))
        {
            var clearing = DocumentPricing.Round(g.Sum(v => v.Clearing));
            var stock = periodic ? clearing : DocumentPricing.Round(g.Sum(v => v.Inventory));
            var account = periodic ? settings.Purchases() : invoice.InventoryAccountCode ?? await settings.InventoryAsync(db, g.Key.Site, ct);
            Add(account, g.Key.Center, stock);
            Add(DefaultAccounts.GoodsNotReceived, g.Key.Center, -clearing);
            if (clearing != stock)
            {
                if (variance == null)
                {
                    var policy = await db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct);
                    variance = policy is { Method: CostingMethod.Standard } or { RecalculateOnNewPurchase: false }
                            && !string.IsNullOrWhiteSpace(policy.StandardCostVarianceAccountCode)
                        ? policy.StandardCostVarianceAccountCode.Trim() : DefaultAccounts.PurchasePriceVariance;
                }
                Add(variance, g.Key.Center, clearing - stock);
            }
        }
        var postingLines = amounts.Where(a => a.Value != 0)
            .Select(a => a.Value > 0 ? new PostingLine(a.Key.Account, a.Value, 0, text, a.Key.Center) : new PostingLine(a.Key.Account, 0, -a.Value, text, a.Key.Center))
            .ToList();
        if (postingLines.Count == 0) return;
        await DefaultAccounts.EnsureAsync(db, ct, postingLines.Select(l => l.AccountCode).Distinct().ToArray());
        await posting.PostAsync(new GenericPostingRequest
        {
            Date = note.Date, Description = $"استلام {text}", SourceType = SourceType, SourceId = note.Id, SourceNumber = note.DeliveryNumber,
            WarehouseId = note.WarehouseId, Lines = postingLines, AllowInactiveCostCenters = true,
        }, ct);
    }

    /// <summary>قيد التكلفة لكل (موقع، مركز): مدين تكلفة مبيعات الموقع / دائن مخزونه، وعكسه للإرجاع. الجرد الدوري بلا قيد (كالبيع).</summary>
    private static async Task PostCostAsync(ErpDbContext db, IAccountingPostingService posting, SalesInvoice invoice, DeliveryNote note,
        List<(Guid? Site, Guid? Center, decimal Value)> costs, string text, CancellationToken ct)
    {
        var settings = await PostingAccounts.SettingsAsync(db, ct);
        if (invoice.InventoryAccountCode == null && settings.IsPeriodic(note.Date)) return;
        var amounts = new Dictionary<(string Account, Guid? Center), decimal>(); // موجب مدين، سالب دائن
        void Add(string account, Guid? center, decimal amount)
        {
            if (amount == 0) return;
            amounts[(account, center)] = amounts.GetValueOrDefault((account, center)) + amount;
        }
        foreach (var g in costs.GroupBy(c => (c.Site, c.Center)))
        {
            var value = DocumentPricing.Round(g.Sum(c => c.Value));
            if (value == 0) continue;
            var site = await PostingAccounts.SiteAsync(db, g.Key.Site, ct);
            var cogs = invoice.CogsAccountCode ?? site.Cogs ?? settings.Cogs();
            var stock = invoice.InventoryAccountCode ?? await settings.InventoryAsync(db, g.Key.Site, ct);
            Add(cogs, g.Key.Center, value);
            Add(stock, g.Key.Center, -value);
        }
        var lines = amounts.Where(a => a.Value != 0)
            .Select(a => a.Value > 0 ? new PostingLine(a.Key.Account, a.Value, 0, text, a.Key.Center) : new PostingLine(a.Key.Account, 0, -a.Value, text, a.Key.Center))
            .ToList();
        if (lines.Count == 0) return;
        await DefaultAccounts.EnsureAsync(db, ct, lines.Select(l => l.AccountCode).Distinct().ToArray());
        await posting.PostAsync(new GenericPostingRequest
        {
            Date = note.Date, Description = $"تكلفة {text}", SourceType = SourceType, SourceId = note.Id, SourceNumber = note.DeliveryNumber,
            WarehouseId = note.WarehouseId, Lines = lines, AllowInactiveCostCenters = true,
        }, ct);
    }

    /// <summary>تشغيلات ما خرج بسطر البيان ناقص ما أُرجع منها، مقتطعة بكمية الإرجاع (فارغ للصنف بلا تشغيلات).</summary>
    private static async Task<List<BatchAllocationDto>?> RemainingBatchesAsync(ErpDbContext db, Guid noteId, Guid itemId, Guid lineId, decimal qty,
        CancellationToken ct)
    {
        var issued = await ItemBatches.OfDocumentAsync(db, SourceType, noteId, itemId, lineId, ct);
        if (issued.Count == 0) return null;
        var returned = (await ItemBatches.OfDocumentAsync(db, ReversalSourceType, noteId, itemId, lineId, ct))
            .GroupBy(b => b.ItemBatchId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
        var pool = issued.Select(b => new BatchAllocationDto
            {
                ItemBatchId = b.ItemBatchId, BatchNumber = b.BatchNumber, ExpiryDate = b.ExpiryDate,
                Quantity = b.Quantity - returned.GetValueOrDefault(b.ItemBatchId),
            })
            .Where(b => b.Quantity > Tolerance).ToList();
        return pool.Count == 0 ? null : ItemBatches.Take(pool, qty);
    }

    /// <summary>سطر الأصل لسطر مرتجع: بمعرّفه إن أُرسل، وإلا أول سطر بالصنف نفسه.</summary>
    public static Guid? OriginOf(IReadOnlyCollection<InvoiceItemBase> original, Guid? lineId, Guid itemId)
        => lineId is Guid id && original.Any(o => o.Id == id) ? id : original.FirstOrDefault(o => o.ItemId == itemId)?.Id;

    /// <summary>المرتجع مالياً (المرتجعات المرحّلة) من كل سطر من الفاتورة بالوحدة الأساسية، عدا <paramref name="excludeReturnId"/>.</summary>
    public static async Task<Dictionary<Guid, decimal>> ReturnedBaseByLineAsync(ErpDbContext db, IReadOnlyCollection<InvoiceItemBase> original, Guid invoiceId,
        Guid? excludeReturnId, CancellationToken ct)
    {
        var except = excludeReturnId ?? Guid.Empty;
        var rows = await db.Set<SalesReturnItem>().AsNoTracking()
            .Where(i => i.Invoice.OriginalInvoiceId == invoiceId && i.Invoice.Status == "posted" && i.InvoiceId != except)
            .Select(i => new { i.OriginalInvoiceItemId, i.ItemId, i.Quantity, i.UnitFactor }).ToListAsync(ct);
        var result = new Dictionary<Guid, decimal>();
        foreach (var r in rows)
            if (OriginOf(original, r.OriginalInvoiceItemId, r.ItemId) is Guid o)
                result[o] = result.GetValueOrDefault(o) + Math.Round(r.Quantity * (r.UnitFactor > 0 ? r.UnitFactor : 1), 4);
        return result;
    }

    /// <summary>
    /// تقليل المسلَّم (إلغاء أو تعديل أو تأكيد بأقل) بعد مرتجع أعاد للمخزون جزءاً مما سُلّم يكرر الإرجاع: يُرفض ما دام المرتجع مالياً
    /// + المسلَّم أكبر من كمية السطر (أي أن المرتجع تجاوز غير المسلَّم).
    /// </summary>
    private static async Task EnsureNoReturnedStockAsync(ErpDbContext db, SalesInvoice invoice, IEnumerable<Guid> lineIds, CancellationToken ct)
    {
        var ids = lineIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var original = invoice.Items.Cast<InvoiceItemBase>().ToList();
        var returned = await ReturnedBaseByLineAsync(db, original, invoice.Id, null, ct);
        if (returned.Count == 0) return;
        var issued = await IssuedByInvoiceLineAsync(db, invoice.Id, ct);
        foreach (var line in invoice.Items.Where(l => ids.Contains(l.Id)))
            if (returned.GetValueOrDefault(line.Id) + issued.GetValueOrDefault(line.Id).Qty > InvoiceLineStock.BaseQuantity(line) + Tolerance)
                throw new ConflictException(string.Format(Messages.DeferredDeliveryReturnedStock, line.ItemName));
    }

    /// <summary>
    /// ما خرج ببيانات الفاتورة لكل سطر منها (بالوحدة الأساسية وقيمته): أساس مرتجع الفاتورة غير المسلّمة (يعود للمخزون ما سُلّم فقط وبتكلفته).
    /// </summary>
    public static async Task<Dictionary<Guid, (decimal Qty, decimal Value)>> IssuedByInvoiceLineAsync(ErpDbContext db, Guid invoiceId, CancellationToken ct)
        => (await db.Set<DeliveryNoteItem>().AsNoTracking()
                .Where(i => i.DeliveryNote.SourceType == DeliveryNoteSources.SalesInvoice && i.DeliveryNote.SourceId == invoiceId
                    && i.SourceLineId != null && i.StockIssuedQty != 0)
                .GroupBy(i => i.SourceLineId!.Value)
                .Select(g => new { g.Key, Qty = g.Sum(i => i.StockIssuedQty), Value = g.Sum(i => i.StockIssuedValue) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Qty, x.Value));
}
