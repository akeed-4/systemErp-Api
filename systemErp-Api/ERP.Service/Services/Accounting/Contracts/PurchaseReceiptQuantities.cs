using ERP.Service.Data;
using ERP.Core.Models.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المصدر الوحيد لكميات استلام المشتريات (إنشاء السند والتحقق والمتابعة والتقارير تقرأ منه):
/// <list type="bullet">
/// <item>المقبول = <c>DeliveredQty</c> (معناه القائم: كمية السند)، والمستلم = <c>ReceivedQty</c> وإلا كمية السند (السجلات القديمة)، والمرفوض = <c>RejectedQty</c>.</item>
/// <item>المرتجع تشغيلياً = <c>ReturnedQty</c> (مرتجع سند الاستلام) ويعود للمتبقي. المسودة والملغى لا يُحتسبان.</item>
/// <item>فاتورة المشتريات: المرتجع مالياً من مرتجعات المشتريات المرحّلة بسطر الأصل (والقديم بلا سطر يوزَّع على أسطر الصنف)، والمتبقي =
/// المطلوب − صافي المقبول − الأكبر من (المرتجع مالياً، المرفوض): المرفوض ينتظر مرتجعه المالي، والمرتجع المالي يقابل المرفوض أولاً، فلا يُحتسبان مرتين.</item>
/// <item>الاتفاقية: المتبقي = المتفق عليه − صافي المقبول (قاعدة <see cref="ContractDeliveries"/> القائمة)، والمرفوض لم يُفوتر فيبقى في المتبقي.</item>
/// </list>
/// </summary>
internal static class PurchaseReceiptQuantities
{
    private const decimal Tolerance = DeliverySources.Tolerance;

    /// <summary>أسطر كل فاتورة مشتريات بكمياتها. <paramref name="exceptNoteId"/>: السند الجاري تعديله، و<paramref name="excludeReturnId"/>: المرتجع المالي الجاري تعديله.</summary>
    public static async Task<Dictionary<Guid, List<PurchaseReceiptSourceLineDto>>> ForInvoicesAsync(ErpDbContext db, IReadOnlyCollection<PurchaseInvoice> invoices,
        Guid? exceptNoteId, CancellationToken ct, Guid? excludeReturnId = null)
    {
        var result = new Dictionary<Guid, List<PurchaseReceiptSourceLineDto>>();
        var ids = invoices.Select(i => i.Id).Distinct().ToList();
        if (ids.Count == 0) return result;

        var skipReturn = excludeReturnId ?? Guid.Empty;
        var returns = await db.Set<PurchaseReturn>().AsNoTracking()
            .Where(r => r.OriginalInvoiceId != null && ids.Contains(r.OriginalInvoiceId.Value) && r.Status == "posted" && r.Id != skipReturn)
            .SelectMany(r => r.Items.Select(l => new { InvoiceId = r.OriginalInvoiceId!.Value, l.OriginalInvoiceItemId, l.ItemId, l.Quantity, l.UnitFactor, l.CreatedAt }))
            .ToListAsync(ct);

        var skipNote = exceptNoteId ?? Guid.Empty;
        var receipts = await DeliverySources.Active(db.Set<DeliveryNoteItem>().AsNoTracking())
            .Where(i => i.DeliveryNote.SourceType == DeliveryNoteSources.PurchaseInvoice && i.DeliveryNote.SourceId != null
                && ids.Contains(i.DeliveryNote.SourceId.Value) && i.DeliveryNoteId != skipNote && i.SourceLineId != null)
            .GroupBy(i => i.SourceLineId!.Value)
            .Select(g => new
            {
                g.Key,
                Accepted = g.Sum(i => i.DeliveredQty),
                Received = g.Sum(i => i.ReceivedQty ?? i.DeliveredQty),
                Rejected = g.Sum(i => i.RejectedQty),
                Returned = g.Sum(i => i.ReturnedQty),
            })
            .ToDictionaryAsync(x => x.Key, ct);

        foreach (var invoice in invoices)
        {
            var lines = invoice.Items.OrderBy(l => l.CreatedAt).ToList();
            var financial = lines.ToDictionary(l => l.Id, _ => 0m);
            foreach (var r in returns.Where(r => r.InvoiceId == invoice.Id).OrderBy(r => r.CreatedAt))
            {
                var baseQty = r.Quantity * (r.UnitFactor <= 0 ? 1 : r.UnitFactor);
                if (r.OriginalInvoiceItemId is Guid lineId && lines.FirstOrDefault(l => l.Id == lineId) is { } own)
                {
                    financial[own.Id] += baseQty / Factor(own);
                    continue;
                }
                foreach (var l in lines.Where(l => l.ItemId == r.ItemId))
                {
                    if (baseQty <= Tolerance) break;
                    var room = (l.Quantity - financial[l.Id]) * Factor(l);
                    var take = Math.Min(room, baseQty);
                    if (take <= 0) continue;
                    financial[l.Id] += take / Factor(l);
                    baseQty -= take;
                }
            }

            result[invoice.Id] = lines.Select(l =>
            {
                var receivable = DeliverySources.IsDeliverable(l.ItemType, l.ItemId);
                var got = receipts.GetValueOrDefault(l.Id);
                var accepted = got?.Accepted ?? 0;
                var returned = got?.Returned ?? 0;
                var rejected = got?.Rejected ?? 0;
                var net = accepted - returned;
                var fin = financial[l.Id];
                return new PurchaseReceiptSourceLineDto
                {
                    SourceLineId = l.Id, ItemId = l.ItemId, ItemName = l.ItemName, Sku = l.Sku, Unit = l.Unit,
                    WarehouseId = l.WarehouseId ?? invoice.WarehouseId, CostCenterId = l.CostCenterId ?? invoice.CostCenterId,
                    UnitPrice = ExclusiveUnitPrice(invoice, l), VatRate = l.VatRate, SourceNet = l.TotalBeforeVat,
                    OrderedQty = l.Quantity, ReceivedQty = Round(got?.Received ?? 0), AcceptedQty = Round(accepted), RejectedQty = Round(rejected),
                    OperationalReturnedQty = Round(returned), NetAcceptedQty = Round(net), FinancialReturnedQty = Round(fin),
                    AwaitingFinancialReturnQty = Math.Max(0, Round(rejected - fin)),
                    RemainingQty = receivable ? Math.Max(0, Round(l.Quantity - net - Math.Max(fin, rejected))) : 0,
                    InvoicedQty = l.Quantity, Receivable = receivable,
                };
            }).ToList();
        }
        return result;
    }

    /// <summary>
    /// بنود كل اتفاقية شراء (مجمّعة بالمفتاح: الصنف، وإلا معرّف البند الوصفي — كقاعدة الاتفاقية) بكميات سنداتها. المفوتر = صافي سندات
    /// فُوترت، والمرتجع مالياً = مرتجعات المشتريات المرحّلة على فواتير تلك السندات (للعرض؛ قاعدة المتبقي للاتفاقية لا تتغير).
    /// </summary>
    public static async Task<Dictionary<Guid, List<PurchaseReceiptSourceLineDto>>> ForContractsAsync(ErpDbContext db, IReadOnlyCollection<CommercialContract> contracts,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, List<PurchaseReceiptSourceLineDto>>();
        var ids = contracts.Select(c => c.Id).Distinct().ToList();
        if (ids.Count == 0) return result;

        var items = await db.Set<CommercialContractItem>().AsNoTracking()
            .Where(i => ids.Contains(i.ContractId) && !i.IsGroup).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var rows = await DeliverySources.Active(db.Set<DeliveryNoteItem>().AsNoTracking())
            .Where(i => i.DeliveryNote.ContractId != null && ids.Contains(i.DeliveryNote.ContractId.Value))
            .Select(i => new
            {
                ContractId = i.DeliveryNote.ContractId!.Value, i.ItemId, Accepted = i.ConfirmedQty ?? i.DeliveredQty,
                Received = i.ReceivedQty ?? (i.ConfirmedQty ?? i.DeliveredQty), i.RejectedQty, i.ReturnedQty, i.DeliveryNote.InvoiceId,
            })
            .ToListAsync(ct);
        var invoiceToContract = rows.Where(r => r.InvoiceId != null).GroupBy(r => r.InvoiceId!.Value).ToDictionary(g => g.Key, g => g.First().ContractId);
        var invoiceIds = invoiceToContract.Keys.ToList();
        var returns = invoiceIds.Count == 0 ? new List<(Guid ContractId, Guid ItemId, decimal Qty)>()
            : (await db.Set<PurchaseReturn>().AsNoTracking()
                    .Where(r => r.OriginalInvoiceId != null && invoiceIds.Contains(r.OriginalInvoiceId.Value) && r.Status == "posted")
                    .SelectMany(r => r.Items.Select(l => new { InvoiceId = r.OriginalInvoiceId!.Value, l.ItemId, l.Quantity }))
                    .ToListAsync(ct))
                .Select(r => (ContractId: invoiceToContract[r.InvoiceId], r.ItemId, Qty: r.Quantity)).ToList();
        var productIds = items.Where(i => i.ItemId != null).Select(i => i.ItemId!.Value).Distinct().ToList();
        var types = productIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ItemType, ct);

        foreach (var contract in contracts)
        {
            result[contract.Id] = items.Where(i => i.ContractId == contract.Id).GroupBy(i => i.ItemId ?? i.Id).Select(g =>
            {
                var first = g.First();
                var mine = rows.Where(r => r.ContractId == contract.Id && r.ItemId == g.Key).ToList();
                var accepted = mine.Sum(r => r.Accepted);
                var returned = mine.Sum(r => r.ReturnedQty);
                var net = accepted - returned;
                var ordered = g.Sum(i => i.Quantity);
                var receivable = first.ItemId is not Guid pid || DeliverySources.IsDeliverable(types.GetValueOrDefault(pid), pid);
                return new PurchaseReceiptSourceLineDto
                {
                    SourceLineId = first.Id, ItemId = g.Key, ItemName = first.Description, Unit = first.Unit,
                    WarehouseId = contract.WarehouseId, CostCenterId = contract.CostCenterId,
                    UnitPrice = first.UnitPrice, VatRate = first.VatRate, SourceNet = g.Sum(i => i.Quantity * i.UnitPrice),
                    OrderedQty = ordered, ReceivedQty = Round(mine.Sum(r => r.Received)), AcceptedQty = Round(accepted),
                    RejectedQty = Round(mine.Sum(r => r.RejectedQty)), OperationalReturnedQty = Round(returned), NetAcceptedQty = Round(net),
                    FinancialReturnedQty = Round(returns.Where(r => r.ContractId == contract.Id && r.ItemId == g.Key).Sum(r => r.Qty)),
                    AwaitingFinancialReturnQty = 0,
                    RemainingQty = receivable ? Math.Max(0, Round(ordered - net)) : 0,
                    InvoicedQty = Round(mine.Where(r => r.InvoiceId != null).Sum(r => r.Accepted - r.ReturnedQty)),
                    Receivable = receivable,
                };
            }).ToList();
        }
        return result;
    }

    /// <summary>
    /// حالة استلام فواتير المشتريات المرحّلة (للعرض، دفعة واحدة لصفحة القائمة) بقيم <see cref="InvoiceDeliveryStatuses"/> وبكميات
    /// <see cref="ForInvoicesAsync"/> نفسها: لا استلام للمشتريات «في الطريق»، فالحالات: لا يحتاج، لم يُستلم، جزئي، بالكامل.
    /// الفاتورة الناتجة عن سندات استلام (اتفاقية أو أمر شراء) مستلمة بسنداتها. الفواتير الأخرى (مسودة، مرتجع) لا تُعاد.
    /// </summary>
    public static async Task<Dictionary<Guid, (string Status, bool Remaining)>> InvoiceReceiptStatusAsync(ErpDbContext db,
        IReadOnlyCollection<PurchaseInvoice> invoices, CancellationToken ct)
    {
        var result = new Dictionary<Guid, (string Status, bool Remaining)>();
        var eligible = invoices.Where(i => i.Kind == InvoiceKind.Purchase && i.Status == "posted" && !i.IsReturn && i.OriginalInvoiceId == null).ToList();
        if (eligible.Count == 0) return result;
        var ids = eligible.Select(i => i.Id).ToList();
        var billedFromReceipts = (await db.Set<DeliveryNote>().AsNoTracking()
            .Where(n => n.InvoiceId != null && ids.Contains(n.InvoiceId.Value)).Select(n => n.InvoiceId!.Value).ToListAsync(ct)).ToHashSet();

        var open = new List<PurchaseInvoice>();
        foreach (var invoice in eligible)
        {
            if (billedFromReceipts.Contains(invoice.Id)
                || invoice.ReferenceType is "delivery_note" or PurchaseOrderReceipts.InvoiceReferenceType)
                result[invoice.Id] = (InvoiceDeliveryStatuses.Delivered, false);
            else open.Add(invoice);
        }
        if (open.Count == 0) return result;

        foreach (var (id, lines) in await ForInvoicesAsync(db, open, null, ct))
        {
            var receivable = lines.Where(l => l.Receivable && l.OrderedQty - l.FinancialReturnedQty > Tolerance).ToList();
            var remaining = receivable.Any(l => l.RemainingQty > Tolerance);
            var status = receivable.Count == 0 ? InvoiceDeliveryStatuses.NotRequired
                : !receivable.Any(l => l.ReceivedQty > Tolerance) ? InvoiceDeliveryStatuses.NotDelivered
                : remaining ? InvoiceDeliveryStatuses.PartiallyDelivered
                : InvoiceDeliveryStatuses.Delivered;
            result[id] = (status, remaining);
        }
        return result;
    }

    /// <summary>حالة الكميات: مستلم بالكامل (لا متبقٍ وقد استُلم شيء)، أو جزئي، أو معلّق.</summary>
    public static string StatusOf(IEnumerable<PurchaseReceiptSourceLineDto> lines)
    {
        var receivable = lines.Where(l => l.Receivable).ToList();
        var anyReceived = receivable.Any(l => l.ReceivedQty > Tolerance);
        if (!anyReceived) return PurchaseSupplyStatuses.Pending;
        return receivable.All(l => l.RemainingQty <= Tolerance) ? PurchaseSupplyStatuses.Received : PurchaseSupplyStatuses.PartiallyReceived;
    }

    /// <summary>سعر الوحدة قبل الضريبة: الفاتورة الشاملة تخزن السعر شاملاً.</summary>
    private static decimal ExclusiveUnitPrice(InvoiceBase invoice, InvoiceItemBase l)
        => invoice.PricingMethod == InvoicePricingMethod.Inclusive && l.VatRate > 0
            ? Math.Round(l.UnitPrice * 100m / (100m + l.VatRate), 4) : l.UnitPrice;

    private static decimal Factor(InvoiceItemBase l) => l.UnitFactor <= 0 ? 1 : l.UnitFactor;
    private static decimal Round(decimal v) => Math.Round(v, 4);
}
