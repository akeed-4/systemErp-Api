using ERP.Service.Data;
using ERP.Core.Models.Shared;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// فاتورة المشتريات المرحّلة كمصدر سند استلام (المسار الثاني: الفوترة ثم الاستلام). السند تشغيلي فقط: الفاتورة أنشأت المخزون
/// والقيد والضريبة والتكلفة، فلا حركة ولا قيد ولا فاتورة من السند. الكميات من <see cref="PurchaseReceiptQuantities"/>، وكل فحص
/// وحجز بقفل مستقل للفاتورة (<see cref="PurchaseInvoiceLockScope"/>) داخل المعاملة.
/// </summary>
internal static partial class DeliverySources
{
    public const string PurchaseInvoiceLockScope = "purchase-invoice-receipt";

    public static Task LockPurchaseInvoiceAsync(ErpDbContext db, Guid invoiceId, CancellationToken ct) => LockAsync(db, PurchaseInvoiceLockScope, invoiceId, ct);

    /// <summary>
    /// فاتورة المشتريات كمصدر: مرحّلة، شراء لا مرتجع، وليست ناتجة عن سندات استلام سبق استلامها وفوترتها (اتفاقية أو أمر شراء)،
    /// فلا تتكرر دورة الاستلام.
    /// </summary>
    public static async Task<PurchaseInvoice> PurchaseInvoiceAsync(ErpDbContext db, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await db.Set<PurchaseInvoice>().AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException(Messages.InvoiceNotFound);
        if (invoice.Status != "posted" || invoice.IsReturn || invoice.OriginalInvoiceId != null || invoice.Kind != InvoiceKind.Purchase)
            throw new ConflictException(Messages.PurchaseReceiptSourceInvoiceNotPosted);
        var fromReceipts = invoice.ReferenceType is "delivery_note" or PurchaseOrderReceipts.InvoiceReferenceType
            || await db.Set<DeliveryNote>().AnyAsync(n => n.InvoiceId == invoiceId, ct);
        if (fromReceipts) throw new ConflictException(Messages.PurchaseReceiptSourceInvoiceFromReceipt);
        return invoice;
    }

    public static async Task<PurchaseReceiptSourceDto> PurchaseInvoiceSourceAsync(ErpDbContext db, PurchaseInvoice invoice, Guid? exceptNoteId, CancellationToken ct)
    {
        var lines = (await PurchaseReceiptQuantities.ForInvoicesAsync(db, new[] { invoice }, exceptNoteId, ct))[invoice.Id];
        return new PurchaseReceiptSourceDto
        {
            SourceType = DeliveryNoteSources.PurchaseInvoice, SourceId = invoice.Id, SourceNumber = invoice.InvoiceNumber, SourceDate = invoice.IssueDate,
            PartyId = invoice.PartyId, PartyName = invoice.PartyName, PartyTaxNumber = invoice.PartyVatNumber, PartyPhone = invoice.PartyPhone,
            WarehouseId = invoice.WarehouseId, CostCenterId = invoice.CostCenterId, Lines = lines,
        };
    }

    /// <summary>
    /// يبني سند الاستلام من فاتورة المشتريات: المورد من الفاتورة، وكل سطر من سطر فاتورته عبر <c>SourceLineId</c> (الصنف والوحدة والسعر
    /// والضريبة والمستودع ومركز التكلفة منه لا من العميل، فلا تتغير)، والمستلم (المقبول + المرفوض) ≤ المتبقي. يقفل الفاتورة.
    /// </summary>
    public static async Task PrepareFromPurchaseInvoiceAsync(ErpDbContext db, DeliveryNote note, Guid invoiceId, CancellationToken ct)
    {
        await LockPurchaseInvoiceAsync(db, invoiceId, ct);
        var invoice = await PurchaseInvoiceAsync(db, invoiceId, ct);
        var source = await PurchaseInvoiceSourceAsync(db, invoice, note.Id == Guid.Empty ? null : note.Id, ct);
        var byLine = source.Lines.ToDictionary(l => l.SourceLineId);

        note.Type = "purchase_delivery";
        note.SourceType = DeliveryNoteSources.PurchaseInvoice;
        note.SourceId = invoice.Id; note.SourceNumber = invoice.InvoiceNumber;
        note.ContractId = null; note.ContractNumber = null; note.RequisitionId = null; note.PurchaseOrderId = null; note.CustodyIssueId = null;
        note.PartyId = invoice.PartyId; note.PartyType = "supplier"; note.PartyName = invoice.PartyName;
        note.PartyTaxNumber = invoice.PartyVatNumber; note.PartyPhone ??= invoice.PartyPhone;
        note.WarehouseId = invoice.WarehouseId;
        note.DeliveryLocationId = null; note.DeliveryAddress = null; note.DeliveryLatitude = null; note.DeliveryLongitude = null;

        if (note.Items.Any(i => i.DeliveredQty < 0 || i.RejectedQty < 0)) throw new ValidationFailedException(Messages.QuantitiesCannotBeNegative);
        if (note.Items.Any(i => i.DeliveredQty + i.RejectedQty <= 0)) throw new ValidationFailedException(Messages.PurchaseReceiptQtyPositive);
        foreach (var g in note.Items.GroupBy(i => i.SourceLineId))
        {
            if (g.Key is not Guid lineId || !byLine.TryGetValue(lineId, out var line))
                throw new ValidationFailedException(Messages.PurchaseReceiptLineNotInInvoice);
            if (!line.Receivable)
                throw new ValidationFailedException(string.Format(Messages.PurchaseReceiptLineNotReceivable, line.ItemName));
            var received = g.Sum(i => i.DeliveredQty + i.RejectedQty);
            if (received > line.RemainingQty + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.PurchaseReceiptExceedsInvoice,
                    line.ItemName, received.ToString("0.####"), line.RemainingQty.ToString("0.####"), line.OrderedQty.ToString("0.####")));
            foreach (var i in g)
            {
                i.ItemId = line.ItemId; i.ItemName = line.ItemName; i.Sku = line.Sku; i.Unit = line.Unit;
                // قيمة السند للعرض من صافي سطر الفاتورة بنسبة المقبول (لا قيد عليها)
                i.UnitPrice = line.UnitPrice; i.VatRate = line.VatRate; i.Discount = ShareOfNet(line.SourceNet, line.OrderedQty, line.UnitPrice, i.DeliveredQty);
                i.WarehouseId = line.WarehouseId; i.CostCenterId = line.CostCenterId;
                i.ContractQty = line.OrderedQty; i.BoqItemId = null;
            }
        }
        ApplyReceiptQuantities(note, allowRejection: true);
    }

    /// <summary>
    /// حقول المستلم والمرفوض في سند الاستلام (يحسبها السرفر): المستلم = المقبول + المرفوض، والمرفوض يتطلب سبباً. المبيعات بلا رفض،
    /// وسندات أوامر الشراء والمشاريع بلا رفض (سلوكها كما كان، والمستلم فارغ = كمية السند).
    /// </summary>
    public static void ApplyReceiptQuantities(DeliveryNote note, bool allowRejection)
    {
        var purchase = note.Type.StartsWith("purchase", StringComparison.Ordinal);
        foreach (var i in note.Items)
        {
            if (i.RejectedQty < 0) throw new ValidationFailedException(Messages.QuantitiesCannotBeNegative);
            if (!purchase)
            {
                i.RejectedQty = 0; i.RejectionReason = null; i.ReceivedQty = null;
                continue;
            }
            if (!allowRejection)
            {
                if (i.RejectedQty > 0) throw new ValidationFailedException(Messages.ReceiptRejectionNotAllowed);
                i.RejectionReason = null; i.ReceivedQty = null;
                continue;
            }
            i.RejectionReason = string.IsNullOrWhiteSpace(i.RejectionReason) ? null : i.RejectionReason.Trim();
            if (i.RejectedQty > 0 && i.RejectionReason == null)
                throw new ValidationFailedException(string.Format(Messages.ReceiptRejectionReasonRequired, i.ItemName));
            if (i.RejectedQty == 0) i.RejectionReason = null;
            i.ReceivedQty = i.DeliveredQty + i.RejectedQty;
        }
    }

    /// <summary>
    /// بعد إعادة كمية للاستلام على فاتورة (حذف مرتجع سند): صافي المقبول + الأكبر من (المرتجع مالياً، المرفوض) لا يتجاوز سطر الفاتورة.
    /// يُحفظ أولاً ثم يُقرأ بقفل الفاتورة.
    /// </summary>
    public static async Task EnsurePurchaseInvoiceRoomAsync(ErpDbContext db, Guid invoiceId, CancellationToken ct)
    {
        await LockPurchaseInvoiceAsync(db, invoiceId, ct);
        await db.SaveChangesAsync(ct);
        var invoice = await db.Set<PurchaseInvoice>().AsNoTracking().Include(i => i.Items).FirstAsync(i => i.Id == invoiceId, ct);
        var lines = (await PurchaseReceiptQuantities.ForInvoicesAsync(db, new[] { invoice }, null, ct))[invoiceId];
        if (lines.FirstOrDefault(l => l.NetAcceptedQty + Math.Max(l.FinancialReturnedQty, l.RejectedQty) > l.OrderedQty + Tolerance) is { } over)
            throw new ConflictException(string.Format(Messages.PurchaseReceiptExceedsInvoice, over.ItemName,
                (over.NetAcceptedQty + over.RejectedQty).ToString("0.####"),
                Math.Max(0, over.OrderedQty - Math.Max(over.FinancialReturnedQty, over.RejectedQty)).ToString("0.####"), over.OrderedQty.ToString("0.####")));
    }

    /// <summary>
    /// مرتجع مشتريات مالي على فاتورة لها سندات استلام: المرتجع يقابل المرفوض أولاً، وما زاد عليه لا يتجاوز غير المقبول في السندات
    /// (المطلوب − صافي المقبول)؛ فالكمية المقبولة تُرجع تشغيلياً بمرتجع السند أولاً ثم مالياً، ولا تُحتسب الكمية مرتين. الفاتورة بلا سندات
    /// كما كانت. يقفل الفاتورة (مرتجع متزامن مع سند لا يقرآن المتبقي نفسه). <paramref name="lines"/> بوحدة سطر الأصل.
    /// </summary>
    public static async Task EnsurePurchaseReturnFitsReceiptsAsync(ErpDbContext db, InvoiceBase original, IReadOnlyCollection<ReturnLineDto> lines,
        Guid? excludeReturnId, CancellationToken ct)
    {
        if (original.Kind != InvoiceKind.Purchase || original.IsReturn) return;
        var hasReceipts = await Active(db.Set<DeliveryNoteItem>().AsNoTracking())
            .AnyAsync(i => i.DeliveryNote.SourceType == DeliveryNoteSources.PurchaseInvoice && i.DeliveryNote.SourceId == original.Id, ct);
        if (!hasReceipts) return;
        await LockPurchaseInvoiceAsync(db, original.Id, ct);
        var invoice = await db.Set<PurchaseInvoice>().AsNoTracking().Include(i => i.Items).FirstAsync(i => i.Id == original.Id, ct);
        var current = (await PurchaseReceiptQuantities.ForInvoicesAsync(db, new[] { invoice }, null, ct, excludeReturnId))[invoice.Id];
        var added = current.ToDictionary(l => l.SourceLineId, _ => 0m);
        foreach (var r in lines.Where(l => l.Quantity > 0))
        {
            if (r.OriginalInvoiceItemId is Guid id && added.ContainsKey(id)) { added[id] += r.Quantity; continue; }
            var left = r.Quantity;
            foreach (var l in current.Where(l => l.ItemId == r.ItemId))
            {
                if (left <= Tolerance) break;
                var take = Math.Min(left, Math.Max(0, l.OrderedQty - l.FinancialReturnedQty - added[l.SourceLineId]));
                added[l.SourceLineId] += take; left -= take;
            }
        }
        foreach (var l in current)
        {
            var extra = added[l.SourceLineId];
            if (extra <= 0) continue;
            if (l.NetAcceptedQty + Math.Max(l.FinancialReturnedQty + extra, l.RejectedQty) > l.OrderedQty + Tolerance)
            {
                var allowed = Math.Max(0, Math.Max(l.OrderedQty - l.NetAcceptedQty - l.FinancialReturnedQty, l.RejectedQty - l.FinancialReturnedQty));
                throw new ConflictException(string.Format(Messages.PurchaseReturnExceedsReceipts, l.ItemName, extra.ToString("0.####"), allowed.ToString("0.####")));
            }
        }
    }

    /// <summary>خصم سطر السند = إجماليه بسعر الوحدة الصافي − نصيبه من صافي سطر الفاتورة (لا سالب).</summary>
    private static decimal ShareOfNet(decimal sourceNet, decimal sourceQty, decimal unitPrice, decimal qty)
    {
        if (sourceQty <= 0 || qty <= 0) return 0;
        var net = DocumentPricing.Round(sourceNet * qty / sourceQty);
        return Math.Max(0, DocumentPricing.Round(qty * unitPrice - net));
    }
}
