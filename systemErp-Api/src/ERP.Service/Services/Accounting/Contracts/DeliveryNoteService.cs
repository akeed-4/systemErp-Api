using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class DeliveryNoteService : IDeliveryNoteService
{
    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ICommercialContractService _contracts;
    private readonly IInvoiceService _invoices;
    private readonly ITransactionRunner _tx;

    public DeliveryNoteService(ErpDbContext db, INumberSequenceService numbers, ICommercialContractService contracts, IInvoiceService invoices, ITransactionRunner tx)
    {
        _db = db; _numbers = numbers; _contracts = contracts; _invoices = invoices; _tx = tx;
    }

    public async Task<PagedResult<DeliveryNoteDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<DeliveryNote>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(n => n.DeliveryNumber.Contains(t) || n.PartyName.Contains(t) || (n.ContractNumber != null && n.ContractNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(n => n.Items).OrderByDescending(n => n.Date)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<DeliveryNoteDto>
        {
            Items = items.Select(Mapper.Map<DeliveryNoteDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<DeliveryNoteDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<DeliveryNoteDto>(await _db.Set<DeliveryNote>().AsNoTracking().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException(Messages.DeliveryNoteNotFound));

    public Task<DeliveryNoteDto> CreateAsync(CreateDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var errors = new List<string>();
            TradeHelper.RequireParty(r.PartyName, errors);
            if (r.Type is not ("sales_delivery" or "purchase_delivery")) errors.Add(Messages.DeliveryNoteTypeInvalid);
            if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneLineRequired);
            if (r.Items.Any(i => i.DeliveredQty < 0 || i.ContractQty < 0)) errors.Add(Messages.QuantitiesCannotBeNegative);
            ValidatePricing(r.Items, errors);
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            CommercialContract? contract = null;
            if (r.ContractId.HasValue)
                contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContractId, token)
                    ?? throw new ValidationFailedException(Messages.ContractNotFoundDot);

            var note = Mapper.Map<DeliveryNote>(r);
            note.DeliveryNumber = await _numbers.NextAsync("delivery_note", r.Type == "sales_delivery" ? "DN-" : "GRN-", token);
            note.ContractNumber = contract?.ContractNumber ?? r.ContractNumber;
            note.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            note.Status = DeliveryNoteStatus.Delivered;
            note.InvoiceId = null; note.InvoiceNumber = null;
            foreach (var i in note.Items) i.ReturnedQty = 0;
            Price(note);
            _db.Add(note);
            await _db.SaveChangesAsync(token);
            return await GetAsync(note.Id, token);
        }, ct);

    public Task<DeliveryNoteDto> UpdateAsync(Guid id, UpdateDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            if (note.InvoiceId != null || note.Status is not (DeliveryNoteStatus.Delivered or DeliveryNoteStatus.Draft))
                throw new ConflictException(Messages.CannotEditBilledOrReturnedNote);
            if (await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token)) throw new ConflictException(Messages.HasReturns);

            var errors = new List<string>();
            TradeHelper.RequireParty(r.PartyName, errors);
            if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneLineRequired);
            if (r.Items.Any(i => i.DeliveredQty < 0 || i.ContractQty < 0)) errors.Add(Messages.QuantitiesCannotBeNegative);
            ValidatePricing(r.Items, errors);
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            var keep = (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber);
            Mapper.Apply(r, note);
            (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber) = keep;
            foreach (var removed in Mapper.SyncCollections(r, note, added => _db.Add(added))) _db.Remove(removed);
            foreach (var i in note.Items) i.ReturnedQty = 0;
            Price(note);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    private static void ValidatePricing(IEnumerable<DeliveryNoteItemDto> items, List<string> errors)
    {
        if (items.Any(i => i.UnitPrice < 0 || i.VatRate is < 0 or > 100)) errors.Add(Messages.PriceAndVatInvalid);
    }

    /// <summary>قيمة البيان تُحتسب في السرفر: الكمية المسلّمة × السعر، والضريبة لكل سطر.</summary>
    private static void Price(DeliveryNote note)
    {
        foreach (var i in note.Items)
        {
            i.TotalBeforeVat = DocumentPricing.Round(i.DeliveredQty * i.UnitPrice);
            i.VatAmount = DocumentPricing.Round(i.TotalBeforeVat * i.VatRate / 100m);
            i.TotalAfterVat = i.TotalBeforeVat + i.VatAmount;
        }
        note.Subtotal = note.Items.Sum(i => i.TotalBeforeVat);
        note.VatTotal = note.Items.Sum(i => i.VatAmount);
        note.GrandTotal = note.Subtotal + note.VatTotal;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            if (note.InvoiceId != null) throw new ConflictException(Messages.CannotDeleteBilledNote);
            if (await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token)) throw new ConflictException(Messages.DeleteReturnsFirst);
            _db.RemoveRange(note.Items);
            _db.Remove(note);
            await _db.SaveChangesAsync(token);
        }, ct);

    public async Task<DeliveryReturnNoteDto> GetReturnAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<DeliveryReturnNoteDto>(await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException(Messages.DeliveryReturnNotFound));

    public Task DeleteReturnAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var ret = await _db.Set<DeliveryReturnNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException(Messages.DeliveryReturnNotFound);
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == ret.DeliveryNoteId, token);
            if (note != null)
            {
                if (note.InvoiceId != null) throw new ConflictException(Messages.OriginalNoteBilled);
                foreach (var line in ret.Items)
                {
                    var src = note.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                    if (src != null) src.ReturnedQty = Math.Max(0, src.ReturnedQty - line.Quantity);
                }
                note.Status = note.Items.Any(i => i.ReturnedQty > 0)
                    ? (note.Items.All(i => i.ReturnedQty >= i.DeliveredQty) ? DeliveryNoteStatus.Returned : DeliveryNoteStatus.PartiallyReturned)
                    : DeliveryNoteStatus.Delivered;
            }
            _db.RemoveRange(ret.Items);
            _db.Remove(ret);
            await _db.SaveChangesAsync(token);
        }, ct);

    public async Task<PagedResult<DeliveryReturnNoteDto>> ListReturnsAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<DeliveryReturnNote>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(n => n.ReturnNumber.Contains(t) || n.DeliveryNumber.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(n => n.Items).OrderByDescending(n => n.Date)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<DeliveryReturnNoteDto>
        {
            Items = items.Select(Mapper.Map<DeliveryReturnNoteDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public Task<DeliveryReturnNoteDto> CreateReturnAsync(CreateDeliveryReturnNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == r.DeliveryNoteId, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            if (note.Status == DeliveryNoteStatus.Invoiced) throw new ConflictException(Messages.CannotReturnBilledNote);
            if (r.Items.Count == 0 || r.Items.Any(i => i.Quantity <= 0)) throw new ValidationFailedException(Messages.ReturnedQuantitiesMustBePositive);
            if (r.Type is not ("sales_delivery_return" or "purchase_delivery_return")) throw new ValidationFailedException(Messages.InvalidType);

            foreach (var line in r.Items)
            {
                var src = note.Items.FirstOrDefault(i => i.ItemId == line.ItemId)
                    ?? throw new ValidationFailedException(Messages.ItemNotInDeliveryNote);
                var remaining = src.DeliveredQty - src.ReturnedQty;
                if (line.Quantity > remaining)
                    throw new ValidationFailedException(string.Format(Messages.ReturnQtyExceedsAvailable, src.ItemName, remaining));
                src.ReturnedQty += line.Quantity;
            }
            note.Status = note.Items.All(i => i.ReturnedQty >= i.DeliveredQty) ? DeliveryNoteStatus.Returned : DeliveryNoteStatus.PartiallyReturned;

            var ret = Mapper.Map<DeliveryReturnNote>(r);
            ret.ReturnNumber = await _numbers.NextAsync("delivery_return", "DR-", token);
            ret.DeliveryNumber = note.DeliveryNumber;
            ret.ContractId = note.ContractId; ret.ContractNumber = note.ContractNumber; ret.PartyName = note.PartyName;
            ret.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            ret.Status = "completed";
            foreach (var i in ret.Items)
            {
                // سعر وضريبة المرتجع من سطر التسليم الأصلي، لا مما يرسله العميل.
                var src = note.Items.First(x => x.ItemId == i.ItemId);
                i.ItemName = src.ItemName; i.Unit = src.Unit; i.UnitPrice = src.UnitPrice; i.VatRate = src.VatRate;
                var net = DocumentPricing.Round(i.Quantity * i.UnitPrice);
                i.VatAmount = DocumentPricing.Round(net * i.VatRate / 100m);
                i.TotalAfterVat = net + i.VatAmount;
            }
            ret.Subtotal = ret.Items.Sum(i => i.TotalAfterVat - i.VatAmount);
            ret.VatTotal = ret.Items.Sum(i => i.VatAmount);
            ret.GrandTotal = ret.Subtotal + ret.VatTotal;
            _db.Add(ret);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<DeliveryReturnNoteDto>(await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(n => n.Items).FirstAsync(n => n.Id == ret.Id, token));
        }, ct);

    public Task<InvoiceDto> CreateInvoiceAsync(Guid deliveryNoteId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == deliveryNoteId, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            if (note.InvoiceId != null) throw new ConflictException(Messages.DeliveryNoteAlreadyBilled);
            var lines = note.Items.Select(i => (Item: i, Qty: i.DeliveredQty - i.ReturnedQty)).Where(x => x.Qty > 0).ToList();
            if (lines.Count == 0) throw new ConflictException(Messages.NoNetQuantitiesToBill);

            // الأصناف المخزنية تُفوتر كأصناف، وغيرها (بنود العقد الوصفية) كبنود خدمة.
            var ids = lines.Select(x => x.Item.ItemId).Distinct().ToList();
            var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(token);
            var sales = note.Type == "sales_delivery";
            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                Kind = sales ? InvoiceKind.Sales : InvoiceKind.Purchase,
                InvoiceType = sales ? TradeHelper.InvoiceTypeFor(note.PartyTaxNumber) : InvoiceType.TaxInvoice,
                PartyId = note.PartyId, PartyName = note.PartyName, PartyVatNumber = note.PartyTaxNumber, PartyPhone = note.PartyPhone,
                WarehouseId = note.WarehouseId,
                PaymentMethod = note.PartyId.HasValue ? PaymentMethod.Credit : PaymentMethod.Cash, Status = "posted",
                Notes = $"فاتورة من بيان التسليم {note.DeliveryNumber}" + (note.ContractNumber != null ? $" - عقد {note.ContractNumber}" : ""),
                ReferenceType = "delivery_note", ReferenceId = note.Id, ReferenceNumber = note.DeliveryNumber,
                Items = lines.Select(x => new InvoiceItemDto
                {
                    ItemId = products.Contains(x.Item.ItemId) ? x.Item.ItemId : Guid.Empty,
                    ItemName = x.Item.ItemName, Sku = x.Item.Sku ?? string.Empty, Unit = x.Item.Unit ?? string.Empty,
                    Quantity = x.Qty, UnitPrice = x.Item.UnitPrice, VatRate = x.Item.VatRate,
                }).ToList(),
            }, token);
            note.InvoiceId = invoice.Id; note.InvoiceNumber = invoice.InvoiceNumber; note.Status = DeliveryNoteStatus.Invoiced;
            await _db.SaveChangesAsync(token);
            return invoice;
        }, ct);

    public Task<InvoiceDto> CreateMilestoneInvoiceAsync(Guid deliveryNoteId, Guid milestoneId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == deliveryNoteId, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            if (note.InvoiceId != null) throw new ConflictException(Messages.DeliveryNoteAlreadyBilled);
            if (note.Status == DeliveryNoteStatus.Returned) throw new ConflictException(Messages.DeliveryNoteFullyReturned);
            if (note.ContractId == null) throw new ValidationFailedException(Messages.DeliveryNoteNotLinkedToContract);
            if (note.Type != "sales_delivery") throw new ConflictException(Messages.MilestoneBillingSalesOnly);

            var invoice = await _contracts.BillMilestoneAsync(note.ContractId.Value, milestoneId, token);
            note.InvoiceId = invoice.Id; note.InvoiceNumber = invoice.InvoiceNumber; note.Status = DeliveryNoteStatus.Invoiced;
            await _db.SaveChangesAsync(token);
            return invoice;
        }, ct);
}
