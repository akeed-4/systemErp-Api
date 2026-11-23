using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Purchasing;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// دورة الشراء على المستندات القائمة: طلب الشراء المعتمد (بلا أسعار) ← <see cref="PurchaseRfq"/> لعدة موردين
/// ← عروض الموردين (<see cref="Quotation"/> purchase_quotation) ← مقارنة السعر والخصم والضريبة والتكاليف الإضافية
/// ← ترسية كاملة أو لكل سطر ← أمر شراء لكل مورد (<see cref="CommercialOrder"/> purchase_order)
/// ← استلام جزئي بسندات استلام (<see cref="PurchaseOrderReceipts"/>) ← فاتورة مورد لكل سند (فوترة جزئية).
/// كل الروابط بالمعرّفات: الطلب ← RFQ ← العرض ← الأمر ← السند ← الفاتورة.
/// </summary>
public class PurchaseCycleService : IPurchaseCycleService
{
    private const decimal Tolerance = 0.00005m;
    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IDeliveryNoteService _notes;

    public PurchaseCycleService(ErpDbContext db, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IDeliveryNoteService notes)
    {
        _db = db; _numbers = numbers; _tx = tx; _user = user; _notes = notes;
    }

    /// <summary>طلب عروض أسعار قائم (مفتوح أو مرسّى) يحجز طلب الشراء عن التحويل المباشر لفاتورة.</summary>
    internal static Task<bool> HasActiveRfqAsync(ErpDbContext db, Guid requisitionId, CancellationToken ct)
        => db.Set<PurchaseRfq>().AnyAsync(r => r.RequisitionId == requisitionId && r.Status != PurchaseRfqStatuses.Cancelled, ct);

    // ---------------- طلب عرض الأسعار ----------------

    public async Task<List<PurchaseRfqDto>> ListRfqsAsync(Guid? requisitionId, string? status, CancellationToken ct = default)
    {
        var q = _db.Set<PurchaseRfq>().AsNoTracking();
        if (requisitionId.HasValue) q = q.Where(r => r.RequisitionId == requisitionId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        var ids = await q.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).Select(r => r.Id).Take(500).ToListAsync(ct);
        return await ToDtosAsync(ids, ct);
    }

    public async Task<PurchaseRfqDto> GetRfqAsync(Guid id, CancellationToken ct = default)
        => (await ToDtosAsync(new List<Guid> { id }, ct)).FirstOrDefault() ?? throw new NotFoundException(Messages.RfqNotFound);

    public Task<PurchaseRfqDto> CreateRfqAsync(CreatePurchaseRfqDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var requisition = await _db.Set<MaterialRequisition>().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == request.RequisitionId, token)
                ?? throw new NotFoundException(Messages.PurchaseRequisitionNotFound);
            if (requisition.Status != "approved" || requisition.ConvertedInvoiceId != null) throw new ConflictException(Messages.RequisitionNotApproved);
            // يقفل الطلب: طلبا عروض متزامنان على الطلب نفسه لا يمرّان معاً
            requisition.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token);
            if (await HasActiveRfqAsync(_db, requisition.Id, token))
                throw new ConflictException(string.Format(Messages.RequisitionHasActiveRfq, requisition.RequisitionNumber));

            var suppliers = await SuppliersAsync(request.SupplierIds, token);
            if (suppliers.Count == 0) throw new ValidationFailedException(Messages.SuppliersRequired);

            var lines = request.Items.Count == 0
                ? requisition.Items.Select(i => (Item: i, Qty: i.ApprovedQuantity ?? i.RequestedQuantity)).ToList()
                : request.Items.Select(x =>
                {
                    var item = requisition.Items.FirstOrDefault(i => i.Id == x.RequisitionItemId);
                    var max = item == null ? 0 : item.ApprovedQuantity ?? item.RequestedQuantity;
                    if (item == null || x.Quantity <= 0 || x.Quantity > max + Tolerance)
                        throw new ValidationFailedException(string.Format(Messages.RfqItemInvalid, item?.ItemName ?? x.RequisitionItemId.ToString()));
                    return (Item: item, Qty: x.Quantity);
                }).ToList();
            if (lines.Count == 0 || lines.Any(l => l.Qty <= 0)) throw new ValidationFailedException(Messages.AtLeastOneItemRequired);
            if (lines.GroupBy(l => l.Item.Id).Any(g => g.Count() > 1))
                throw new ValidationFailedException(string.Format(Messages.RfqItemInvalid, lines.GroupBy(l => l.Item.Id).First(g => g.Count() > 1).First().Item.ItemName));

            var date = request.Date?.Date ?? SaudiTime.Today;
            var rfq = new PurchaseRfq
            {
                RequisitionId = requisition.Id, ContractId = requisition.ContractId, WarehouseId = requisition.WarehouseId,
                Date = date, DueDate = request.DueDate?.Date, Notes = Trim(request.Notes), CreatedByUserId = _user.UserId,
                RfqNumber = await _numbers.AssignAsync(new DocumentNumberRequest
                {
                    Key = "purchase_rfq", DefaultPrefix = "RFQ-", WarehouseId = requisition.WarehouseId, Date = date,
                    Exists = (n, c) => _db.Set<PurchaseRfq>().AnyAsync(x => x.RfqNumber == n, c),
                }, token),
            };
            foreach (var (item, qty) in lines)
                rfq.Items.Add(new PurchaseRfqItem
                {
                    RequisitionItemId = item.Id, ItemId = item.ItemId, ItemName = item.ItemName, Sku = item.Sku, Unit = item.Unit, Quantity = qty,
                });
            foreach (var s in suppliers) rfq.Suppliers.Add(new PurchaseRfqSupplier { SupplierId = s.Id, SupplierName = s.NameAr });
            _db.Add(rfq);
            await _db.SaveChangesAsync(token);
            return await GetRfqAsync(rfq.Id, token);
        }, ct);

    public Task<PurchaseRfqDto> AddSuppliersAsync(Guid rfqId, AddRfqSuppliersDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rfq = await _db.Set<PurchaseRfq>().Include(r => r.Suppliers).FirstOrDefaultAsync(r => r.Id == rfqId, token)
                ?? throw new NotFoundException(Messages.RfqNotFound);
            if (rfq.Status != PurchaseRfqStatuses.Open) throw new ConflictException(Messages.RfqNotOpen);
            await EnsureNoPendingAwardAsync(rfq.Id, token);
            var suppliers = await SuppliersAsync(request.SupplierIds, token);
            if (suppliers.Count == 0) throw new ValidationFailedException(Messages.SuppliersRequired);
            foreach (var s in suppliers.Where(s => rfq.Suppliers.All(x => x.SupplierId != s.Id)))
                rfq.Suppliers.Add(new PurchaseRfqSupplier { SupplierId = s.Id, SupplierName = s.NameAr });
            await _db.SaveChangesAsync(token);
            return await GetRfqAsync(rfq.Id, token);
        }, ct);

    /// <summary>الإلغاء قبل أي استلام: تُلغى أوامر الترسية وتُرفض العروض، ويعود طلب الشراء قابلاً لطلب عروض جديد أو للتحويل.</summary>
    public Task<PurchaseRfqDto> CancelRfqAsync(Guid rfqId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rfq = await _db.Set<PurchaseRfq>().FirstOrDefaultAsync(r => r.Id == rfqId, token) ?? throw new NotFoundException(Messages.RfqNotFound);
            if (rfq.Status == PurchaseRfqStatuses.Cancelled) throw new ConflictException(Messages.RfqNotOpen);
            await EnsureNoPendingAwardAsync(rfq.Id, token);
            var orders = await _db.Set<CommercialOrder>().Where(o => o.RfqId == rfqId).ToListAsync(token);
            foreach (var o in orders) await PurchaseOrderReceipts.LockAsync(_db, o.Id, token);
            var orderIds = orders.Select(o => (Guid?)o.Id).ToList();
            if (orderIds.Count > 0 && await _db.Set<DeliveryNote>().AnyAsync(n => orderIds.Contains(n.PurchaseOrderId), token))
                throw new ConflictException(Messages.RfqHasReceipts);
            foreach (var o in orders) o.Status = "cancelled";
            foreach (var q in await _db.Set<Quotation>().Where(q => q.RfqId == rfqId).ToListAsync(token)) q.Status = "rejected";
            rfq.Status = PurchaseRfqStatuses.Cancelled;
            await _db.SaveChangesAsync(token);
            return await GetRfqAsync(rfq.Id, token);
        }, ct);

    // ---------------- عروض الموردين ----------------

    /// <summary>عرض المورد على الطلب (أو تحديث عرضه القائم ما دام الطلب مفتوحاً): السعر والخصم والضريبة لكل سطر، والتكاليف الإضافية.</summary>
    public Task<QuotationDto> SubmitQuotationAsync(Guid rfqId, SubmitSupplierQuotationDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rfq = await _db.Set<PurchaseRfq>().Include(r => r.Items).Include(r => r.Suppliers).FirstOrDefaultAsync(r => r.Id == rfqId, token)
                ?? throw new NotFoundException(Messages.RfqNotFound);
            if (rfq.Status != PurchaseRfqStatuses.Open) throw new ConflictException(Messages.RfqNotOpen);
            await EnsureNoPendingAwardAsync(rfq.Id, token);
            if (rfq.Suppliers.All(s => s.SupplierId != request.SupplierId)) throw new ValidationFailedException(Messages.SupplierNotInvited);
            var supplier = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId, token)
                ?? throw new NotFoundException(Messages.PurchaseCycleSupplierInactive);

            var lines = request.Lines.Where(l => l.UnitPrice > 0).ToList();
            if (lines.Count == 0) throw new ValidationFailedException(Messages.QuotationLinesRequired);
            if (request.ExtraCosts < 0) throw new ValidationFailedException(Messages.ExtraCostsInvalid);
            var date = request.Date?.Date ?? SaudiTime.Today;
            var validUntil = request.ValidUntil?.Date ?? date.AddDays(30);
            if (validUntil < date) throw new ValidationFailedException(Messages.ValidUntilInvalid);

            var items = new List<QuotationItem>();
            foreach (var l in lines)
            {
                var rfqItem = rfq.Items.FirstOrDefault(i => i.Id == l.RfqItemId)
                    ?? throw new ValidationFailedException(string.Format(Messages.RfqItemInvalid, l.RfqItemId));
                if (items.Any(i => i.RfqItemId == rfqItem.Id)) throw new ValidationFailedException(string.Format(Messages.QuotationLineDuplicate, rfqItem.ItemName));
                var qty = l.Quantity <= 0 ? rfqItem.Quantity : l.Quantity;
                if (qty > rfqItem.Quantity + Tolerance || l.Discount < 0 || l.Discount > qty * l.UnitPrice || l.VatRate is < 0 or > 100)
                    throw new ValidationFailedException(string.Format(Messages.QuotationLineInvalid, rfqItem.ItemName));
                items.Add(new QuotationItem
                {
                    RfqItemId = rfqItem.Id, ItemId = rfqItem.ItemId, ItemName = rfqItem.ItemName, Sku = rfqItem.Sku, Unit = rfqItem.Unit,
                    Quantity = qty, UnitPrice = l.UnitPrice, Discount = l.Discount, VatRate = l.VatRate,
                });
            }

            var quotation = await _db.Set<Quotation>().Include(q => q.Items)
                .FirstOrDefaultAsync(q => q.RfqId == rfqId && q.PartyId == supplier.Id && q.Status != "rejected", token);
            if (quotation == null)
            {
                quotation = new Quotation
                {
                    Type = "purchase_quotation", RfqId = rfqId, CreatedByUserId = _user.UserId,
                    QuotationNumber = await _numbers.AssignAsync(new DocumentNumberRequest
                    {
                        Key = "purchase_quotation", DefaultPrefix = "PQT-", WarehouseId = rfq.WarehouseId, Date = date,
                        ManualNumber = request.QuotationNumber, UserEntered = true,
                        Exists = (n, c) => _db.Set<Quotation>().AnyAsync(q => q.QuotationNumber == n, c),
                    }, token),
                };
                _db.Add(quotation);
            }
            else
            {
                _db.RemoveRange(quotation.Items);
                quotation.Items.Clear();
            }
            quotation.PartyId = supplier.Id; quotation.PartyType = "supplier"; quotation.PartyName = supplier.NameAr;
            quotation.PartyPhone = supplier.Phone; quotation.PartyEmail = supplier.Email; quotation.PartyVatNumber = supplier.VatNumber;
            quotation.WarehouseId = rfq.WarehouseId;
            quotation.Date = date; quotation.ValidUntil = validUntil; quotation.Status = "sent";
            quotation.ExtraCosts = DocumentPricing.Round(request.ExtraCosts); quotation.DeliveryDays = request.DeliveryDays;
            quotation.PaymentTerms = Trim(request.PaymentTerms); quotation.Notes = Trim(request.Notes);
            foreach (var i in items) quotation.Items.Add(i);

            var priced = DocumentPricing.Price(quotation.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate)).ToList());
            var list = quotation.Items.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                list[i].TotalBeforeVat = priced.Lines[i].Net; list[i].VatAmount = priced.Lines[i].VatAmount; list[i].TotalAfterVat = priced.Lines[i].Total;
            }
            quotation.Subtotal = priced.NetTotal; quotation.DiscountTotal = priced.DiscountTotal;
            quotation.VatTotal = priced.VatTotal; quotation.GrandTotal = priced.GrandTotal;
            await _db.SaveChangesAsync(token);
            return Mapper.Map<QuotationDto>(await _db.Set<Quotation>().AsNoTracking().Include(q => q.Items).FirstAsync(q => q.Id == quotation.Id, token));
        }, ct);

    // ---------------- المقارنة ----------------

    /// <summary>
    /// مقارنة العروض: لكل سطر سعر كل مورد وخصمه وضريبته ونصيبه من التكاليف الإضافية (بنسبة صافي السطر في عرضه) وتكلفة الوحدة الشاملة،
    /// والأفضل لكل سطر (بكامل الكمية)، والأفضل إجمالاً بين العروض المغطية لكل الأسطر.
    /// </summary>
    public async Task<RfqComparisonDto> CompareAsync(Guid rfqId, CancellationToken ct = default)
    {
        var rfq = await _db.Set<PurchaseRfq>().AsNoTracking().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == rfqId, ct)
            ?? throw new NotFoundException(Messages.RfqNotFound);
        var quotes = await _db.Set<Quotation>().AsNoTracking().Include(q => q.Items)
            .Where(q => q.RfqId == rfqId).OrderBy(q => q.CreatedAt).ToListAsync(ct);
        var result = new RfqComparisonDto { RfqId = rfq.Id, RfqNumber = rfq.RfqNumber };
        var offers = new Dictionary<Guid, List<RfqComparisonOfferDto>>();
        foreach (var q in quotes)
        {
            var costed = Cost(q, q.Items.ToList());
            var coversAll = rfq.Items.All(ri => q.Items.Any(i => i.RfqItemId == ri.Id && i.Quantity >= ri.Quantity - Tolerance));
            result.Quotes.Add(new RfqComparisonQuoteDto
            {
                QuotationId = q.Id, QuotationNumber = q.QuotationNumber, SupplierId = q.PartyId ?? Guid.Empty, SupplierName = q.PartyName,
                ValidUntil = q.ValidUntil, DeliveryDays = q.DeliveryDays, PaymentTerms = q.PaymentTerms, Status = q.Status,
                Gross = costed.Sum(c => DocumentPricing.Round(c.Item.Quantity * c.Item.UnitPrice)), Discount = costed.Sum(c => c.Item.Discount),
                Net = costed.Sum(c => c.Net), ExtraCosts = q.ExtraCosts, Vat = costed.Sum(c => c.Vat), GrandTotal = costed.Sum(c => c.Total),
                LinesQuoted = q.Items.Count, CoversAll = coversAll,
            });
            foreach (var c in costed.Where(c => c.Item.RfqItemId.HasValue))
            {
                var rfqItem = rfq.Items.FirstOrDefault(i => i.Id == c.Item.RfqItemId);
                if (rfqItem == null) continue;
                if (!offers.TryGetValue(rfqItem.Id, out var list)) offers[rfqItem.Id] = list = new();
                list.Add(new RfqComparisonOfferDto
                {
                    QuotationId = q.Id, QuotationItemId = c.Item.Id, SupplierName = q.PartyName, Quantity = c.Item.Quantity,
                    UnitPrice = c.Item.UnitPrice, Discount = c.Item.Discount, VatRate = c.Item.VatRate, Net = c.Net, ExtraShare = c.Extra,
                    Vat = c.Vat, Total = c.Total, LandedUnitPrice = c.LandedUnit, FullQuantity = c.Item.Quantity >= rfqItem.Quantity - Tolerance,
                });
            }
        }
        foreach (var ri in rfq.Items.OrderBy(i => i.ItemName))
        {
            var list = offers.GetValueOrDefault(ri.Id) ?? new();
            result.Lines.Add(new RfqComparisonLineDto
            {
                RfqItemId = ri.Id, ItemId = ri.ItemId, ItemName = ri.ItemName, Unit = ri.Unit, Quantity = ri.Quantity, Offers = list,
                BestQuotationId = list.Where(o => o.FullQuantity).OrderBy(o => o.LandedUnitPrice * (1 + o.VatRate / 100m)).Select(o => (Guid?)o.QuotationId).FirstOrDefault(),
            });
        }
        result.BestTotalQuotationId = result.Quotes.Where(q => q.CoversAll && q.Status != "rejected").OrderBy(q => q.GrandTotal)
            .Select(q => (Guid?)q.QuotationId).FirstOrDefault();
        return result;
    }

    private sealed record CostedLine(QuotationItem Item, decimal Net, decimal Extra, decimal Vat, decimal Total, decimal LandedUnit);

    /// <summary>
    /// تكلفة أسطر عرض كله: الصافي بعد الخصم، ونصيب التكاليف الإضافية بنسبة صافي السطر من صافي العرض (الباقي على آخر سطر)،
    /// والضريبة على الصافي مع نصيبه. الترسية الجزئية تأخذ أنصبة أسطرها من هذا الحساب فلا تحمل نصيب الأسطر غير المرسّاة.
    /// </summary>
    private static List<CostedLine> Cost(Quotation q, List<QuotationItem> items)
    {
        items = items.OrderBy(i => i.ItemName, StringComparer.Ordinal).ThenBy(i => i.Id).ToList();
        var nets = items.Select(i => DocumentPricing.Round(i.Quantity * i.UnitPrice - i.Discount)).ToList();
        var netTotal = nets.Sum();
        var result = new List<CostedLine>();
        decimal allocated = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var extra = i == items.Count - 1 ? q.ExtraCosts - allocated
                : netTotal == 0 ? 0 : DocumentPricing.Round(q.ExtraCosts * nets[i] / netTotal);
            allocated += extra;
            var vat = DocumentPricing.Round((nets[i] + extra) * items[i].VatRate / 100m);
            var landed = items[i].Quantity <= 0 ? 0 : Math.Round((nets[i] + extra) / items[i].Quantity, 4);
            result.Add(new CostedLine(items[i], nets[i], extra, vat, nets[i] + extra + vat, landed));
        }
        return result;
    }

    // ---------------- الترسية ----------------

    /// <summary>
    /// الترسية (صلاحية الاعتماد): كل سطر على عرض يسعّره بكامل كميته وما زال صالحاً، ثم أمر شراء مؤكد لكل مورد بأسعار عرضه
    /// ونصيب تكاليفه الإضافية، وتُعلَّم العروض المرسّاة محوّلة والبقية مرفوضة.
    /// </summary>
    public Task<PurchaseRfqDto> AwardAsync(Guid rfqId, AwardRfqDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var rfq = await _db.Set<PurchaseRfq>().Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == rfqId, token)
                ?? throw new NotFoundException(Messages.RfqNotFound);
            if (rfq.Status != PurchaseRfqStatuses.Open) throw new ConflictException(Messages.RfqNotOpen);
            // يقفل الطلب بتعديله أولاً: ترسيتان متزامنتان لا تنشئان أوامر مكررة
            rfq.Status = PurchaseRfqStatuses.Awarded;
            rfq.AwardedBy = _user.Name; rfq.AwardedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(token);

            var quotes = await _db.Set<Quotation>().Include(q => q.Items).Where(q => q.RfqId == rfqId && q.Status != "rejected").ToListAsync(token);
            if (quotes.Count == 0) throw new ConflictException(Messages.NoQuotations);
            var assignment = new Dictionary<Guid, Guid>(); // سطر RFQ ← العرض
            if (request.QuotationId is Guid whole)
            {
                if (quotes.All(q => q.Id != whole)) throw new ValidationFailedException(Messages.AwardQuotationInvalid);
                foreach (var ri in rfq.Items) assignment[ri.Id] = whole;
            }
            else if (request.Lines.Count > 0)
            {
                foreach (var l in request.Lines)
                {
                    if (rfq.Items.All(i => i.Id != l.RfqItemId)) throw new ValidationFailedException(string.Format(Messages.RfqItemInvalid, l.RfqItemId));
                    if (quotes.All(q => q.Id != l.QuotationId)) throw new ValidationFailedException(Messages.AwardQuotationInvalid);
                    assignment[l.RfqItemId] = l.QuotationId;
                }
            }
            else throw new ValidationFailedException(Messages.AwardRequired);

            foreach (var ri in rfq.Items)
            {
                if (!assignment.TryGetValue(ri.Id, out var qid)) throw new ValidationFailedException(string.Format(Messages.AwardLineMissing, ri.ItemName));
                var q = quotes.First(x => x.Id == qid);
                if (!q.Items.Any(i => i.RfqItemId == ri.Id && i.Quantity >= ri.Quantity - Tolerance))
                    throw new ValidationFailedException(string.Format(Messages.AwardLineNotQuoted, ri.ItemName));
            }

            var today = SaudiTime.Today;
            foreach (var group in assignment.GroupBy(a => a.Value))
            {
                var q = quotes.First(x => x.Id == group.Key);
                if (q.ValidUntil < today) throw new ConflictException(string.Format(Messages.AwardQuotationExpired, q.PartyName, q.ValidUntil));
                // نصيب كل سطر من التكاليف الإضافية محسوب على العرض كله بنسبة قيمته؛ الأمر يحمل نصيب أسطره المرسّاة فقط
                var costed = Cost(q, q.Items.ToList()).Where(c => group.Any(g => g.Key == c.Item.RfqItemId)).ToList();
                var order = new CommercialOrder
                {
                    Type = PurchaseOrderReceipts.OrderType, PartyId = q.PartyId, PartyType = "supplier", PartyName = q.PartyName,
                    PartyPhone = q.PartyPhone, PartyVatNumber = q.PartyVatNumber, WarehouseId = rfq.WarehouseId,
                    OrderDate = today, ExpectedDeliveryDate = request.ExpectedDeliveryDate?.Date ?? today.AddDays(q.DeliveryDays ?? 7),
                    PaymentTerms = q.PaymentTerms, Status = "confirmed", CreatedByUserId = _user.UserId,
                    Notes = $"أمر شراء من ترسية {rfq.RfqNumber} على عرض {q.QuotationNumber}",
                    RfqId = rfq.Id, QuotationId = q.Id, RequisitionId = rfq.RequisitionId, ExtraCosts = costed.Sum(c => c.Extra),
                    OrderNumber = await _numbers.AssignAsync(new DocumentNumberRequest
                    {
                        Key = PurchaseOrderReceipts.OrderType, DefaultPrefix = "PO-", WarehouseId = rfq.WarehouseId, Date = today,
                        Exists = (n, c) => _db.Set<CommercialOrder>().AnyAsync(o => o.OrderNumber == n, c),
                    }, token),
                };
                foreach (var c in costed)
                {
                    var rfqItem = rfq.Items.First(i => i.Id == c.Item.RfqItemId);
                    // لا تتجاوز الترسية كمية طلب العروض (العرض بكامل الكمية شرط أعلاه)
                    if (c.Item.Quantity > rfqItem.Quantity + Tolerance)
                        throw new ValidationFailedException(string.Format(Messages.QuotationLineInvalid, rfqItem.ItemName));
                    order.Items.Add(new CommercialOrderItem
                    {
                        ItemId = c.Item.ItemId, ItemName = c.Item.ItemName, Sku = c.Item.Sku, Unit = c.Item.Unit,
                        Quantity = c.Item.Quantity, UnitPrice = c.Item.UnitPrice, Discount = c.Item.Discount, VatRate = c.Item.VatRate,
                        TotalBeforeVat = c.Net, VatAmount = c.Vat, TotalAfterVat = c.Total, LandedUnitPrice = c.LandedUnit,
                        RfqItemId = rfqItem.Id, RequisitionItemId = rfqItem.RequisitionItemId, QuotationItemId = c.Item.Id,
                    });
                }
                order.Subtotal = costed.Sum(c => c.Net + c.Extra);
                order.VatTotal = costed.Sum(c => c.Vat);
                order.GrandTotal = order.Subtotal + order.VatTotal;
                _db.Add(order);
                q.Status = "converted_to_order";
            }
            foreach (var q in quotes.Where(q => assignment.Values.All(v => v != q.Id))) q.Status = "rejected";
            await _db.SaveChangesAsync(token);
            return await GetRfqAsync(rfq.Id, token);
        }, ct);

    // ---------------- أمر الشراء: الاستلام والفوترة الجزئية ----------------

    public async Task<PurchaseOrderTrackingDto> OrderTrackingAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await PurchaseOrderAsync(orderId, ct);
        var progress = await PurchaseOrderReceipts.ProgressAsync(_db, orderId, ct);
        var receipts = await _db.Set<DeliveryNote>().AsNoTracking().Where(n => n.PurchaseOrderId == orderId).OrderBy(n => n.Date).ThenBy(n => n.CreatedAt)
            .Select(n => new PurchaseOrderReceiptDto
            {
                Id = n.Id, DeliveryNumber = n.DeliveryNumber, Date = n.Date, GrandTotal = n.GrandTotal, InvoiceId = n.InvoiceId, InvoiceNumber = n.InvoiceNumber,
            }).ToListAsync(ct);
        var dto = new PurchaseOrderTrackingDto
        {
            Id = order.Id, OrderNumber = order.OrderNumber, Status = order.Status, SupplierId = order.PartyId, SupplierName = order.PartyName,
            WarehouseId = order.WarehouseId, ExtraCosts = order.ExtraCosts, GrandTotal = order.GrandTotal,
            RequisitionId = order.RequisitionId, RfqId = order.RfqId, QuotationId = order.QuotationId, Receipts = receipts,
            RequisitionNumber = order.RequisitionId == null ? null
                : await _db.Set<MaterialRequisition>().AsNoTracking().Where(r => r.Id == order.RequisitionId).Select(r => r.RequisitionNumber).FirstOrDefaultAsync(ct),
            RfqNumber = order.RfqId == null ? null
                : await _db.Set<PurchaseRfq>().AsNoTracking().Where(r => r.Id == order.RfqId).Select(r => r.RfqNumber).FirstOrDefaultAsync(ct),
            QuotationNumber = order.QuotationId == null ? null
                : await _db.Set<Quotation>().AsNoTracking().Where(q => q.Id == order.QuotationId).Select(q => q.QuotationNumber).FirstOrDefaultAsync(ct),
        };
        foreach (var (itemId, line) in PurchaseOrderReceipts.Lines(order))
        {
            var p = progress.GetValueOrDefault(itemId);
            dto.Lines.Add(new PurchaseOrderTrackingLineDto
            {
                ItemId = itemId, ItemName = line.First.ItemName, Unit = line.First.Unit, Ordered = line.Ordered,
                Received = p.Received, Invoiced = p.Invoiced, RemainingToReceive = Math.Max(0, line.Ordered - p.Received),
                ReceivedNotInvoiced = Math.Max(0, p.Received - p.Invoiced), UnitPrice = line.UnitPrice, VatRate = line.VatRate,
            });
        }
        return dto;
    }

    /// <summary>استلام جزئي: سند استلام مشتريات على الأمر بالكميات المستلمة (القواعد والقفل في <see cref="PurchaseOrderReceipts"/>).</summary>
    public async Task<DeliveryNoteDto> ReceiveAsync(Guid orderId, ReceivePurchaseOrderDto request, CancellationToken ct = default)
    {
        var order = await PurchaseOrderAsync(orderId, ct);
        var lines = request.Lines.Where(l => l.Quantity > 0).ToList();
        if (lines.Count == 0) throw new ValidationFailedException(Messages.ReceiptQtyPositive);
        // أمر شراء لطلب مشروع مقاولات: الاستلام في موقع المشروع كبيان استلام الطلب المباشر (موقع التوريد في الطلب)،
        // والطلب القديم «موقع المشروع» عهدة مباشرة للمشروع
        var project = order.RequisitionId is not { } requisitionId ? null
            : await _db.Set<MaterialRequisition>().AsNoTracking().Where(r => r.Id == requisitionId && r.ContractId != null)
                .Select(r => new { r.Id, r.DeliveryLocation }).FirstOrDefaultAsync(ct);
        return await _notes.CreateAsync(new CreateDeliveryNoteDto
        {
            RequisitionId = project?.Id,
            ConstructionWarehouseReceipt = project != null && project.DeliveryLocation != RequisitionDeliveryLocations.ProjectSite,
            Type = ProjectReceipts.NoteType, PurchaseOrderId = order.Id, PartyId = order.PartyId, PartyName = order.PartyName,
            DeliveryNumber = request.DeliveryNumber ?? string.Empty, Date = request.Date?.Date ?? default,
            WarehouseId = request.WarehouseId ?? order.WarehouseId, Notes = Trim(request.Notes) ?? $"استلام على أمر الشراء {order.OrderNumber}",
            Items = lines.Select(l => new DeliveryNoteItemDto
            {
                ItemId = l.ItemId, DeliveredQty = l.Quantity,
                ItemName = order.Items.FirstOrDefault(i => i.ItemId == l.ItemId)?.ItemName ?? string.Empty,
            }).ToList(),
        }, ct);
    }

    /// <summary>فوترة جزئية: فاتورة المورد لسند استلام واحد من الأمر بكمياته الصافية.</summary>
    public async Task<InvoiceDto> InvoiceReceiptAsync(Guid orderId, Guid deliveryNoteId, DateTime? date, CancellationToken ct = default)
    {
        await PurchaseOrderAsync(orderId, ct);
        if (!await _db.Set<DeliveryNote>().AnyAsync(n => n.Id == deliveryNoteId && n.PurchaseOrderId == orderId, ct))
            throw new ValidationFailedException(Messages.ReceiptNotOfOrder);
        return await _notes.CreateInvoiceAsync(deliveryNoteId, date, null, ct);
    }

    /// <summary>فوترة مجمّعة: فاتورة مورد واحدة لعدة سندات استلام من الأمر (القفل ومنع التكرار في <see cref="IDeliveryNoteService"/>).</summary>
    public async Task<InvoiceDto> InvoiceReceiptsAsync(Guid orderId, InvoicePurchaseReceiptsDto request, CancellationToken ct = default)
    {
        await PurchaseOrderAsync(orderId, ct);
        return await _notes.CreatePurchaseOrderInvoiceAsync(orderId, request.DeliveryNoteIds, request.Date, ct);
    }

    // ---------------- مساعدات ----------------

    /// <summary>ترسية محجوزة بانتظار سياسة الاعتماد: العروض والموردون ثابتون كما رآها المعتمِد حتى القرار.</summary>
    private async Task EnsureNoPendingAwardAsync(Guid rfqId, CancellationToken ct)
    {
        if (await _db.Set<ApprovalRequest>().AnyAsync(r => r.DocumentId == rfqId && r.Status == "pending", ct))
            throw new ConflictException(Messages.RfqAwardPendingApproval);
    }

    private async Task<CommercialOrder> PurchaseOrderAsync(Guid orderId, CancellationToken ct)
        => await _db.Set<CommercialOrder>().AsNoTracking().Include(o => o.Items)
               .FirstOrDefaultAsync(o => o.Id == orderId && o.Type == PurchaseOrderReceipts.OrderType, ct)
           ?? throw new NotFoundException(Messages.PurchaseCycleOrderNotFound);

    private async Task<List<Supplier>> SuppliersAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Where(id => id != Guid.Empty).Distinct().ToList();
        var suppliers = await _db.Set<Supplier>().AsNoTracking().Where(s => distinct.Contains(s.Id)).ToListAsync(ct);
        if (suppliers.Count != distinct.Count || suppliers.Any(s => s.Status != "active")) throw new ValidationFailedException(Messages.PurchaseCycleSupplierInactive);
        return suppliers;
    }

    private async Task<List<PurchaseRfqDto>> ToDtosAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new();
        var rfqs = await _db.Set<PurchaseRfq>().AsNoTracking().Include(r => r.Items).Include(r => r.Suppliers)
            .Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        var reqIds = rfqs.Select(r => r.RequisitionId).Distinct().ToList();
        var reqNumbers = await _db.Set<MaterialRequisition>().AsNoTracking().Where(r => reqIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RequisitionNumber, ct);
        var rfqIds = rfqs.Select(r => (Guid?)r.Id).ToList();
        var quotes = await _db.Set<Quotation>().AsNoTracking().Include(q => q.Items).Where(q => rfqIds.Contains(q.RfqId)).ToListAsync(ct);
        var orders = await _db.Set<CommercialOrder>().AsNoTracking().Where(o => rfqIds.Contains(o.RfqId))
            .Select(o => new { o.RfqId, Dto = new PurchaseOrderSummaryDto
            {
                Id = o.Id, OrderNumber = o.OrderNumber, SupplierId = o.PartyId, SupplierName = o.PartyName, Status = o.Status,
                GrandTotal = o.GrandTotal, ExtraCosts = o.ExtraCosts,
            } }).ToListAsync(ct);
        // ترسية محجوزة بطلب اعتماد معلّق: الواجهة تعرضها وتقفل العروض حتى القرار
        var pendingAward = (await _db.Set<ApprovalRequest>().AsNoTracking().Where(a => ids.Contains(a.DocumentId) && a.Status == "pending")
            .Select(a => a.DocumentId).ToListAsync(ct)).ToHashSet();
        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return rfqs.OrderBy(r => order[r.Id]).Select(r =>
        {
            var own = quotes.Where(q => q.RfqId == r.Id).ToList();
            return new PurchaseRfqDto
            {
                Id = r.Id, RfqNumber = r.RfqNumber, RequisitionId = r.RequisitionId, RequisitionNumber = reqNumbers.GetValueOrDefault(r.RequisitionId),
                ContractId = r.ContractId, WarehouseId = r.WarehouseId, Date = r.Date, DueDate = r.DueDate, Status = r.Status, Notes = r.Notes,
                AwardedBy = r.AwardedBy, AwardedAt = r.AwardedAt, QuotationCount = own.Count, AwardPendingApproval = pendingAward.Contains(r.Id),
                Items = r.Items.OrderBy(i => i.ItemName).Select(i => new PurchaseRfqItemDto
                {
                    Id = i.Id, RequisitionItemId = i.RequisitionItemId, ItemId = i.ItemId, ItemName = i.ItemName, Sku = i.Sku, Unit = i.Unit, Quantity = i.Quantity,
                }).ToList(),
                Suppliers = r.Suppliers.OrderBy(s => s.SupplierName).Select(s =>
                {
                    var q = own.Where(x => x.PartyId == s.SupplierId).OrderBy(x => x.Status == "rejected").FirstOrDefault();
                    return new PurchaseRfqSupplierDto
                    {
                        SupplierId = s.SupplierId, SupplierName = s.SupplierName, QuotationId = q?.Id, QuotationNumber = q?.QuotationNumber,
                        QuotationStatus = q?.Status,
                        // الإجمالي شامل التكاليف الإضافية وضريبتها كما في المقارنة
                        QuotationTotal = q == null ? null : Cost(q, q.Items.ToList()).Sum(c => c.Total),
                    };
                }).ToList(),
                Orders = orders.Where(o => o.RfqId == r.Id).Select(o => o.Dto).ToList(),
            };
        }).ToList();
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(500, v.Trim().Length)];
}
