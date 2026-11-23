using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الاستلام على أمر شراء (<see cref="DeliveryNote.PurchaseOrderId"/>): الاستلام الجزئي بعدة سندات، يُفوتر كل سند وحده أو عدة سندات بفاتورة مورد واحدة.
/// المستلم والمفوتر محسوبان من السندات نفسها (صافي المرتجع)، فحذف سند أو إرجاعه أو حذف فاتورته يعيد الأرقام تلقائياً بلا عدّادات تنحرف.
/// كل استلام يقفل الأمر (<see cref="LockAsync"/>) فلا يتجاوز استلامان متزامنان المتبقي.
/// </summary>
internal static class PurchaseOrderReceipts
{
    public const string OrderType = "purchase_order";
    /// <summary>مرجع فاتورة المورد المجمّعة لعدة سندات استلام من الأمر (المرجع = الأمر، والسندات مربوطة بها بـ InvoiceId).</summary>
    public const string InvoiceReferenceType = "purchase_order_receipts";
    private const decimal Tolerance = 0.00005m;

    public static async Task LockAsync(ErpDbContext db, Guid orderId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Purchase order lock requires an active transaction.");
        var resource = $"po:{db.CurrentTenantId:N}:{orderId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'purchase order lock timeout', 1;",
            ct);
    }

    /// <summary>صافي المستلم والمفوتر لكل صنف من سندات الأمر (عدا سند بعينه).</summary>
    public static async Task<Dictionary<Guid, (decimal Received, decimal Invoiced)>> ProgressAsync(ErpDbContext db, Guid orderId, CancellationToken ct, Guid? exceptNoteId = null)
        => (await db.Set<DeliveryNoteItem>().AsNoTracking()
                .Where(i => i.DeliveryNote.PurchaseOrderId == orderId && (exceptNoteId == null || i.DeliveryNoteId != exceptNoteId))
                .Select(i => new { i.ItemId, Q = i.DeliveredQty - i.ReturnedQty, Billed = i.DeliveryNote.InvoiceId != null })
                .ToListAsync(ct))
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.Q), g.Where(x => x.Billed).Sum(x => x.Q)));

    /// <summary>سعر وحدة الاستلام قبل الضريبة لكل صنف في الأمر: صافي الأسطر (بعد الخصم ومع نصيب التكاليف الإضافية) ÷ كميتها.</summary>
    public static Dictionary<Guid, (decimal Ordered, decimal UnitPrice, decimal VatRate, CommercialOrderItem First)> Lines(CommercialOrder order)
        => order.Items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g =>
        {
            var qty = g.Sum(i => i.Quantity);
            var value = g.Sum(i => i.LandedUnitPrice is decimal landed ? landed * i.Quantity : i.TotalBeforeVat);
            return (qty, qty <= 0 ? 0 : Math.Round(value / qty, 4), g.First().VatRate, g.First());
        });

    /// <summary>
    /// يتحقق من سند استلام على أمر شراء ويكمله: الأمر مؤكد وغير مفوتر كاملاً، والمورد والسعر والضريبة من الأمر،
    /// وكمية كل صنف ≤ المطلوب في الأمر − المستلم بسنداته الأخرى. طلب الشراء لمشروع يُمرَّر فيصير السند استلام مشروع (عهدة).
    /// </summary>
    public static async Task PrepareAsync(ErpDbContext db, DeliveryNote note, CancellationToken ct)
    {
        if (note.PurchaseOrderId is not { } orderId) return;
        if (note.Type != ProjectReceipts.NoteType) throw new ValidationFailedException(Messages.ReceiptOnlyPurchase);
        await LockAsync(db, orderId, ct);
        var order = await db.Set<CommercialOrder>().AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException(Messages.PurchaseCycleOrderNotFound);
        if (order.Type != OrderType) throw new ValidationFailedException(Messages.PurchaseCycleOrderNotFound);
        if (order.ConvertedInvoiceId != null) throw new ConflictException(Messages.OrderInvoicedDirectly);
        if (order.Status is not ("confirmed" or "partially_fulfilled"))
            throw new ConflictException(string.Format(Messages.OrderNotReceivable, order.OrderNumber));
        if (note.PartyId.HasValue && order.PartyId.HasValue && note.PartyId != order.PartyId)
            throw new ValidationFailedException(Messages.ReceiptSupplierMismatch);

        note.PartyId = order.PartyId; note.PartyType = "supplier";
        note.PartyName = order.PartyName; note.PartyTaxNumber = order.PartyVatNumber; note.PartyPhone = order.PartyPhone;
        note.WarehouseId ??= order.WarehouseId;
        if (note.RequisitionId.HasValue && note.RequisitionId != order.RequisitionId)
            throw new ValidationFailedException(Messages.ReceiptNotOfOrder);
        note.RequisitionId = null;
        // طلب شراء لمشروع: السند استلام مشروع (عهدة أو موقع المشروع) بقواعد ProjectReceipts
        if (order.RequisitionId is { } requisitionId
            && await db.Set<MaterialRequisition>().AsNoTracking().AnyAsync(r => r.Id == requisitionId && r.ContractId != null, ct))
            note.RequisitionId = requisitionId;

        var lines = Lines(order);
        var before = await ProgressAsync(db, orderId, ct);
        foreach (var item in note.Items)
        {
            if (!lines.TryGetValue(item.ItemId, out var line))
                throw new ValidationFailedException(string.Format(Messages.ItemNotInOrder, item.ItemName, order.OrderNumber));
            item.ItemName = line.First.ItemName; item.Sku = line.First.Sku; item.Unit = line.First.Unit;
            item.UnitPrice = line.UnitPrice; item.VatRate = line.VatRate;
            item.ContractQty = line.Ordered;
        }
        foreach (var g in note.Items.GroupBy(i => i.ItemId))
        {
            var qty = g.Sum(i => i.DeliveredQty);
            if (qty <= 0) throw new ValidationFailedException(Messages.ReceiptQtyPositive);
            var ordered = lines[g.Key].Ordered;
            var received = before.GetValueOrDefault(g.Key).Received;
            if (qty > ordered - received + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.ReceiptExceedsOrder,
                    g.First().ItemName, qty.ToString("0.####"), Math.Max(0, ordered - received).ToString("0.####"), ordered.ToString("0.####")));
        }
    }

    /// <summary>صافي المستلم لكل صنف لا يتجاوز كمية الأمر (بعد إلغاء مرتجع مثلاً).</summary>
    public static async Task EnsureWithinOrderAsync(ErpDbContext db, Guid orderId, CancellationToken ct)
    {
        var order = await db.Set<CommercialOrder>().AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null) return;
        var progress = await ProgressAsync(db, orderId, ct);
        foreach (var (itemId, line) in Lines(order))
        {
            var received = progress.GetValueOrDefault(itemId).Received;
            if (received > line.Ordered + Tolerance)
                throw new ConflictException(string.Format(Messages.ReceiptExceedsOrder,
                    line.First.ItemName, received.ToString("0.####"), line.Ordered.ToString("0.####"), line.Ordered.ToString("0.####")));
        }
    }

    /// <summary>حالة الأمر من أرقامه: مؤكد ← مستلم جزئياً ← مكتمل (استُلم كله وفوتر كله).</summary>
    public static async Task RefreshStatusAsync(ErpDbContext db, Guid orderId, CancellationToken ct)
    {
        var order = await db.Set<CommercialOrder>().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order == null || order.Status is "cancelled" or "draft" || order.ConvertedInvoiceId != null) return;
        var progress = await ProgressAsync(db, orderId, ct);
        var lines = Lines(order);
        var anyReceived = progress.Values.Any(p => p.Received > Tolerance);
        var allDone = lines.All(l => progress.GetValueOrDefault(l.Key).Received >= l.Value.Ordered - Tolerance
                                     && progress.GetValueOrDefault(l.Key).Invoiced >= l.Value.Ordered - Tolerance);
        order.Status = allDone ? "completed" : anyReceived ? "partially_fulfilled" : "confirmed";
    }
}
