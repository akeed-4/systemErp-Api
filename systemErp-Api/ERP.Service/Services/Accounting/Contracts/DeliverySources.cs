using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Core.Models.Shared;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مصدرا بيان التسليم ومعادلة الكميات:
/// <list type="bullet">
/// <item>كمية البيان الفعّالة للسطر = (المؤكد تسليمه إن أُكّد، وإلا كمية البيان) − مرتجع التسليم. الملغى والمسودة لا يُحتسبان،
/// فإلغاء البيان يحرر كمياته المحجوزة دون كتابة.</item>
/// <item>فاتورة بيع مرحّلة: المتبقي لسطرها = كمية السطر − المرتجع مالياً (إشعارات دائنة مرحّلة) − الفعّال في بياناتها الأخرى.
/// مرتجع التسليم يعيد الكمية للتسليم، والمرتجع المالي يخرجها من التسليم: لا يُخلط بينهما.</item>
/// </list>
/// الفحص والحجز داخل معاملة بقفل المصدر (<c>sp_getapplock</c>) فلا يقرأ طلبان متزامنان المتبقي نفسه.
/// </summary>
internal static partial class DeliverySources
{
    public const decimal Tolerance = 0.00005m;

    /// <summary>أنواع الأصناف التي تُسلَّم مادياً؛ الخدمة والبند الوصفي (بلا صنف) لا يُسلَّمان ببيان ويبقيان في الفاتورة.</summary>
    private static readonly string[] PhysicalTypes =
        { ProductItemTypes.Stock, ProductItemTypes.Manufactured, ProductItemTypes.Kit, ProductItemTypes.Composite };

    public static async Task LockAsync(ErpDbContext db, string scope, Guid id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Delivery lock requires an active transaction.");
        var resource = $"{scope}:{db.CurrentTenantId:N}:{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'delivery lock timeout', 1;",
            ct);
    }

    /// <summary>قفل البيان نفسه لعمليات التوصيل والفوترة عليه (تأكيد/إلغاء/فوترة متزامنة).</summary>
    public static Task LockNoteAsync(ErpDbContext db, Guid noteId, CancellationToken ct) => LockAsync(db, "delivery-note", noteId, ct);

    public static bool IsDeliverable(string? itemType, Guid itemId)
        => itemId != Guid.Empty && PhysicalTypes.Contains(itemType ?? ProductItemTypes.Stock);

    /// <summary>البيانات التي تحجز كمياتها: لا المسودة ولا الملغى.</summary>
    public static IQueryable<DeliveryNoteItem> Active(IQueryable<DeliveryNoteItem> q)
        => q.Where(i => i.DeliveryNote.Status != DeliveryNoteStatus.Draft && i.DeliveryNote.Status != DeliveryNoteStatus.Cancelled);

    // ---------------- فاتورة البيع المرحّلة ----------------

    /// <summary>
    /// فاتورة البيع كمصدر: مرحّلة، بيع لا مرتجع، وليست ناتجة عن بيان تسليم (فلا تتكرر دورة التسليم والفوترة).
    /// </summary>
    public static async Task<SalesInvoice> SalesInvoiceAsync(ErpDbContext db, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await db.Set<SalesInvoice>().AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException(Messages.InvoiceNotFound);
        if (invoice.Status != "posted" || invoice.IsReturn || invoice.OriginalInvoiceId != null || invoice.Kind != InvoiceKind.Sales)
            throw new ConflictException(Messages.DeliverySourceInvoiceNotPosted);
        var fromNote = invoice.ReferenceType == "delivery_note"
            || await db.Set<DeliveryNote>().AnyAsync(n => n.InvoiceId == invoiceId, ct)
            || await db.Set<DeliveryNoteInvoiceLine>().AnyAsync(l => l.InvoiceId == invoiceId, ct);
        if (fromNote) throw new ConflictException(Messages.DeliverySourceInvoiceFromDeliveryNote);
        return invoice;
    }

    /// <summary>أسطر الفاتورة بكمياتها: المفوتر، المرتجع مالياً، الفعّال في البيانات (عدا <paramref name="exceptNoteId"/>)، والمتبقي.</summary>
    public static async Task<DeliverySourceDto> SalesInvoiceSourceAsync(ErpDbContext db, SalesInvoice invoice, Guid? exceptNoteId, CancellationToken ct)
    {
        var lines = invoice.Items.OrderBy(l => l.CreatedAt).ToList();
        var returns = await FinancialReturnLinesAsync(db, new[] { invoice.Id }, ct);
        var financial = FinancialReturned(lines, returns.GetValueOrDefault(invoice.Id) ?? new());

        var noteId = exceptNoteId ?? Guid.Empty;
        var delivered = await Active(db.Set<DeliveryNoteItem>().AsNoTracking())
            .Where(i => i.DeliveryNote.SourceType == DeliveryNoteSources.SalesInvoice && i.DeliveryNote.SourceId == invoice.Id
                && i.DeliveryNoteId != noteId && i.SourceLineId != null)
            .GroupBy(i => i.SourceLineId!.Value)
            .Select(g => new { g.Key, Qty = g.Sum(i => (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        return new DeliverySourceDto
        {
            SourceType = DeliveryNoteSources.SalesInvoice, SourceId = invoice.Id, SourceNumber = invoice.InvoiceNumber,
            PartyId = invoice.PartyId, PartyName = invoice.PartyName, PartyTaxNumber = invoice.PartyVatNumber, PartyPhone = invoice.PartyPhone,
            WarehouseId = invoice.WarehouseId, CostCenterId = invoice.CostCenterId,
            Lines = lines.Select(l =>
            {
                var deliverable = IsDeliverable(l.ItemType, l.ItemId);
                var done = delivered.GetValueOrDefault(l.Id);
                return new DeliverySourceLineDto
                {
                    SourceLineId = l.Id, ItemId = l.ItemId, ItemName = l.ItemName, Sku = l.Sku, Unit = l.Unit,
                    WarehouseId = l.WarehouseId ?? invoice.WarehouseId, CostCenterId = l.CostCenterId ?? invoice.CostCenterId,
                    UnitPrice = ExclusiveUnitPrice(invoice, l), VatRate = l.VatRate, SourceNet = l.TotalBeforeVat, SourceQty = l.Quantity,
                    FinancialReturnedQty = Round(financial[l.Id]), DeliveredQty = Round(done),
                    RemainingQty = deliverable ? Math.Max(0, Round(l.Quantity - financial[l.Id] - done)) : 0,
                    Deliverable = deliverable,
                };
            }).ToList(),
        };
    }

    /// <summary>
    /// يبني بيان التسليم من فاتورة البيع: الطرف من الفاتورة، وكل سطر من سطر فاتورته (الصنف والوحدة والسعر والضريبة والموقع ومركز التكلفة
    /// منه لا من العميل، فلا يتغير موقع الصرف)، والكمية ≤ المتبقي. يُستدعى داخل المعاملة؛ يقفل الفاتورة.
    /// </summary>
    public static async Task PrepareFromSalesInvoiceAsync(ErpDbContext db, DeliveryNote note, Guid invoiceId, CancellationToken ct)
    {
        await LockAsync(db, "sales-invoice-delivery", invoiceId, ct);
        var invoice = await SalesInvoiceAsync(db, invoiceId, ct);
        var source = await SalesInvoiceSourceAsync(db, invoice, note.Id == Guid.Empty ? null : note.Id, ct);
        var byLine = source.Lines.ToDictionary(l => l.SourceLineId);

        note.Type = "sales_delivery";
        note.SourceType = DeliveryNoteSources.SalesInvoice;
        note.SourceId = invoice.Id; note.SourceNumber = invoice.InvoiceNumber;
        note.ContractId = null; note.ContractNumber = null; note.RequisitionId = null; note.PurchaseOrderId = null;
        note.PartyId = invoice.PartyId; note.PartyType = "customer"; note.PartyName = invoice.PartyName;
        note.PartyTaxNumber = invoice.PartyVatNumber; note.PartyPhone ??= invoice.PartyPhone;
        note.WarehouseId = invoice.WarehouseId;

        foreach (var g in note.Items.GroupBy(i => i.SourceLineId))
        {
            if (g.Key is not Guid lineId || !byLine.TryGetValue(lineId, out var line))
                throw new ValidationFailedException(Messages.DeliveryLineNotInSourceInvoice);
            if (!line.Deliverable)
                throw new ValidationFailedException(string.Format(Messages.DeliveryLineNotDeliverable, line.ItemName));
            var qty = g.Sum(i => i.DeliveredQty);
            if (qty > line.RemainingQty + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.DeliveryExceedsInvoice,
                    line.ItemName, qty.ToString("0.####"), line.RemainingQty.ToString("0.####"), line.SourceQty.ToString("0.####")));
            foreach (var i in g)
            {
                i.ItemId = line.ItemId; i.ItemName = line.ItemName; i.Sku = line.Sku; i.Unit = line.Unit;
                // قيمة البيان من صافي سطر الفاتورة بنسبة الكمية: خصم السطر وخصم الفاتورة والسعر الشامل لا يرفعونها عن الفاتورة
                i.UnitPrice = line.UnitPrice; i.VatRate = line.VatRate; i.Discount = ShareDiscount(line, i.DeliveredQty);
                i.WarehouseId = line.WarehouseId; i.CostCenterId = line.CostCenterId;
                i.ContractQty = line.SourceQty;
            }
        }
        if (note.Items.Any(i => i.DeliveredQty <= 0)) throw new ValidationFailedException(Messages.DeliveryQtyPositive);
    }

    private sealed record ReturnLine(Guid? OriginalInvoiceItemId, Guid ItemId, decimal Quantity, decimal UnitFactor);

    /// <summary>أسطر مرتجعات المبيعات المرحّلة (الإشعارات الدائنة) لكل فاتورة.</summary>
    private static async Task<Dictionary<Guid, List<ReturnLine>>> FinancialReturnLinesAsync(ErpDbContext db, IReadOnlyCollection<Guid> invoiceIds,
        CancellationToken ct)
    {
        var rows = await db.Set<SalesReturn>().AsNoTracking()
            .Where(r => r.OriginalInvoiceId != null && invoiceIds.Contains(r.OriginalInvoiceId.Value) && r.Status == "posted")
            .SelectMany(r => r.Items.Select(l => new { InvoiceId = r.OriginalInvoiceId!.Value, l.OriginalInvoiceItemId, l.ItemId, l.Quantity, l.UnitFactor }))
            .ToListAsync(ct);
        return rows.GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => g.Select(r => new ReturnLine(r.OriginalInvoiceItemId, r.ItemId, r.Quantity, r.UnitFactor)).ToList());
    }

    /// <summary>المرتجع مالياً لكل سطر بوحدة السطر: بسطره الأصلي، والقديم بلا سطر أصل يوزَّع على أسطر الصنف بالترتيب.</summary>
    private static Dictionary<Guid, decimal> FinancialReturned(List<SalesInvoiceItem> lines, IEnumerable<ReturnLine> returns)
    {
        var financial = lines.ToDictionary(l => l.Id, _ => 0m);
        foreach (var r in returns)
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
        return financial;
    }

    /// <summary>
    /// حالة تسليم فواتير البيع المرحّلة (للعرض، دفعة واحدة لصفحة القائمة) بمعادلة المتبقي نفسها في <see cref="SalesInvoiceSourceAsync"/>:
    /// المطلوب = كمية السطر المادي − المرتجع مالياً، والمسلَّم = الفعّال في البيانات النشطة. الفاتورة الناتجة عن بيان تسليم مسلّمة ببيانها.
    /// الفواتير الأخرى (مسودة، مرتجع، غير بيع) لا تُعاد.
    /// </summary>
    public static async Task<Dictionary<Guid, (string Status, bool Remaining)>> SalesInvoiceDeliveryAsync(ErpDbContext db,
        IReadOnlyCollection<SalesInvoice> invoices, CancellationToken ct)
    {
        var result = new Dictionary<Guid, (string Status, bool Remaining)>();
        var eligible = invoices.Where(i => i.Kind == InvoiceKind.Sales && i.Status == "posted" && !i.IsReturn && i.OriginalInvoiceId == null).ToList();
        if (eligible.Count == 0) return result;
        var ids = eligible.Select(i => i.Id).ToList();

        var fromNote = (await db.Set<DeliveryNote>().AsNoTracking()
                .Where(n => n.InvoiceId != null && ids.Contains(n.InvoiceId.Value)).Select(n => n.InvoiceId!.Value).ToListAsync(ct))
            .Concat(await db.Set<DeliveryNoteInvoiceLine>().AsNoTracking()
                .Where(l => ids.Contains(l.InvoiceId)).Select(l => l.InvoiceId).ToListAsync(ct))
            .ToHashSet();
        var returns = await FinancialReturnLinesAsync(db, ids, ct);
        var noteLines = (await Active(db.Set<DeliveryNoteItem>().AsNoTracking())
                .Where(i => i.DeliveryNote.SourceType == DeliveryNoteSources.SalesInvoice && i.DeliveryNote.SourceId != null
                    && ids.Contains(i.DeliveryNote.SourceId.Value) && i.SourceLineId != null)
                .Select(i => new
                {
                    InvoiceId = i.DeliveryNote.SourceId!.Value, LineId = i.SourceLineId!.Value,
                    Effective = (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty, i.DeliveryNote.DeliveryStatus,
                })
                .ToListAsync(ct))
            .ToLookup(x => x.InvoiceId);

        foreach (var invoice in eligible)
        {
            if (fromNote.Contains(invoice.Id) || invoice.ReferenceType == "delivery_note")
            {
                result[invoice.Id] = (InvoiceDeliveryStatuses.Delivered, false);
                continue;
            }
            var lines = invoice.Items.OrderBy(l => l.CreatedAt).ToList();
            var financial = FinancialReturned(lines, returns.GetValueOrDefault(invoice.Id) ?? new());
            var notes = noteLines[invoice.Id].ToList();
            var doneByLine = notes.GroupBy(n => n.LineId).ToDictionary(g => g.Key, g => g.Sum(n => n.Effective));

            decimal required = 0;
            var remaining = false;
            foreach (var l in lines.Where(l => IsDeliverable(l.ItemType, l.ItemId)))
            {
                var need = Math.Max(0, l.Quantity - financial[l.Id]);
                required += need;
                if (need - doneByLine.GetValueOrDefault(l.Id) > Tolerance) remaining = true;
            }
            var active = notes.Where(n => n.Effective > Tolerance).ToList();
            var pending = active.Any(n => n.DeliveryStatus is DeliveryTrackingStatuses.Pending or DeliveryTrackingStatuses.InTransit);
            var confirmed = active.Any(n => DeliveryTrackingStatuses.IsConfirmed(n.DeliveryStatus));

            var status = required <= Tolerance ? InvoiceDeliveryStatuses.NotRequired
                : active.Count == 0 ? InvoiceDeliveryStatuses.NotDelivered
                : !remaining && !pending ? InvoiceDeliveryStatuses.Delivered
                : confirmed ? InvoiceDeliveryStatuses.PartiallyDelivered
                : InvoiceDeliveryStatuses.InProgress;
            result[invoice.Id] = (status, remaining && required > Tolerance);
        }
        return result;
    }

    // ---------------- الاتفاقية ----------------

    /// <summary>بنود الاتفاقية بالمتفق عليه والفعّال في بياناتها والمتبقي (المفتاح: الصنف، وإلا معرّف البند الوصفي).</summary>
    public static async Task<DeliverySourceDto> ContractSourceAsync(ErpDbContext db, CommercialContract contract, CancellationToken ct)
    {
        var items = await db.Set<CommercialContractItem>().AsNoTracking()
            .Where(i => i.ContractId == contract.Id && !i.IsGroup).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var delivered = await Active(db.Set<DeliveryNoteItem>().AsNoTracking())
            .Where(i => i.DeliveryNote.ContractId == contract.Id)
            .GroupBy(i => i.ItemId)
            .Select(g => new { g.Key, Qty = g.Sum(i => (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var types = await ItemTypesAsync(db, items.Where(i => i.ItemId != null).Select(i => i.ItemId!.Value), ct);
        var used = new Dictionary<Guid, decimal>();
        var lines = new List<DeliverySourceLineDto>();
        foreach (var i in items)
        {
            var key = i.ItemId ?? i.Id;
            // الصنف المكرر في الاتفاقية: صافي المسلَّم يُستهلك من بنوده بالترتيب
            var pool = delivered.GetValueOrDefault(key) - used.GetValueOrDefault(key);
            var done = Math.Max(0, Math.Min(pool, i.Quantity));
            used[key] = used.GetValueOrDefault(key) + done;
            lines.Add(new DeliverySourceLineDto
            {
                SourceLineId = i.Id, ItemId = key, ItemName = i.Description, Unit = i.Unit, UnitPrice = i.UnitPrice, VatRate = i.VatRate,
                WarehouseId = contract.WarehouseId, CostCenterId = contract.CostCenterId,
                SourceQty = i.Quantity, DeliveredQty = Round(done), RemainingQty = Math.Max(0, Round(i.Quantity - done)),
                Deliverable = i.ItemId is Guid pid && IsDeliverable(types.GetValueOrDefault(pid), pid) || i.ItemId == null,
            });
        }
        return new DeliverySourceDto
        {
            SourceType = DeliveryNoteSources.Contract, SourceId = contract.Id, SourceNumber = contract.ContractNumber,
            PartyId = contract.PartyId, PartyName = contract.PartyName, PartyPhone = contract.PartyPhone,
            WarehouseId = contract.WarehouseId, CostCenterId = contract.CostCenterId, Lines = lines,
        };
    }

    private static async Task<Dictionary<Guid, string>> ItemTypesAsync(ErpDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new()
            : await db.Set<Product>().AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ItemType, ct);
    }

    // ---------------- الفوترة والحالة ----------------

    /// <summary>المتاح للفوترة المرحلية من سطر بيان متتبَّع: المؤكد − مرتجع التسليم − ما فُوتر.</summary>
    public static decimal Billable(DeliveryNoteItem i) => Math.Max(0, (i.ConfirmedQty ?? 0) - i.ReturnedQty - i.InvoicedQty);

    /// <summary>حالة الفوترة/المرتجع لبيان متتبَّع من كميات أسطره (لا يمس الملغى).</summary>
    public static void RefreshTrackedStatus(DeliveryNote note)
    {
        if (note.Status == DeliveryNoteStatus.Cancelled) return;
        decimal Basis(DeliveryNoteItem i) => i.ConfirmedQty ?? i.DeliveredQty;
        if (note.Items.Any(i => i.InvoicedQty > 0))
            note.Status = DeliveryTrackingStatuses.IsConfirmed(note.DeliveryStatus) && note.Items.All(i => Billable(i) <= Tolerance)
                ? DeliveryNoteStatus.Invoiced : DeliveryNoteStatus.PartiallyInvoiced;
        else if (note.Items.Any(i => i.ReturnedQty > 0))
            note.Status = note.Items.All(i => i.ReturnedQty >= Basis(i) - Tolerance) ? DeliveryNoteStatus.Returned : DeliveryNoteStatus.PartiallyReturned;
        else note.Status = DeliveryNoteStatus.Delivered;
    }

    public static void AddEvent(ErpDbContext db, DeliveryNote note, ICurrentUser user, string type, string? from, string? to, string? notes = null)
        => db.Add(new DeliveryNoteEvent
        {
            DeliveryNoteId = note.Id, EventType = type, FromStatus = from, ToStatus = to, Notes = notes,
            UserId = user.UserId, UserName = user.Name ?? user.Email, OccurredAt = DateTime.UtcNow,
        });

    public static Task<List<DeliveryNoteEventDto>> EventsAsync(ErpDbContext db, Guid noteId, CancellationToken ct)
        => db.Set<DeliveryNoteEvent>().AsNoTracking().Where(e => e.DeliveryNoteId == noteId).OrderBy(e => e.OccurredAt).ThenBy(e => e.CreatedAt)
            .Select(e => new DeliveryNoteEventDto
            {
                Id = e.Id, DeliveryNoteId = e.DeliveryNoteId, EventType = e.EventType, FromStatus = e.FromStatus, ToStatus = e.ToStatus,
                Notes = e.Notes, UserId = e.UserId, UserName = e.UserName, OccurredAt = e.OccurredAt,
            }).ToListAsync(ct);

    /// <summary>سعر الوحدة قبل الضريبة: الفاتورة الشاملة تخزن السعر شاملاً.</summary>
    private static decimal ExclusiveUnitPrice(SalesInvoice invoice, SalesInvoiceItem l)
        => invoice.PricingMethod == InvoicePricingMethod.Inclusive && l.VatRate > 0
            ? Math.Round(l.UnitPrice * 100m / (100m + l.VatRate), 4) : l.UnitPrice;

    /// <summary>خصم سطر البيان = إجماليه بسعر الوحدة الصافي − نصيبه من صافي سطر الفاتورة (لا سالب).</summary>
    private static decimal ShareDiscount(DeliverySourceLineDto line, decimal qty)
    {
        if (line.SourceQty <= 0 || qty <= 0) return 0;
        var net = DocumentPricing.Round(line.SourceNet * qty / line.SourceQty);
        return Math.Max(0, DocumentPricing.Round(qty * line.UnitPrice - net));
    }

    private static decimal Factor(SalesInvoiceItem l) => l.UnitFactor <= 0 ? 1 : l.UnitFactor;
    private static decimal Round(decimal v) => Math.Round(v, 4);
}
