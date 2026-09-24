using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class DeliveryNoteService : IDeliveryNoteService
{
    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ICommercialContractService _contracts;
    private readonly ITransactionRunner _tx;

    public DeliveryNoteService(ErpDbContext db, INumberSequenceService numbers, ICommercialContractService contracts, ITransactionRunner tx)
    {
        _db = db; _numbers = numbers; _contracts = contracts; _tx = tx;
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
            ?? throw new NotFoundException("بيان التسليم غير موجود"));

    public Task<DeliveryNoteDto> CreateAsync(CreateDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var errors = new List<string>();
            TradeHelper.RequireParty(r.PartyName, errors);
            if (r.Type is not ("sales_delivery" or "purchase_delivery")) errors.Add("النوع: sales_delivery | purchase_delivery.");
            if (r.Items.Count == 0) errors.Add("يجب إدخال بند واحد على الأقل.");
            if (r.Items.Any(i => i.DeliveredQty < 0 || i.ContractQty < 0)) errors.Add("الكميات لا تكون سالبة.");
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            CommercialContract? contract = null;
            if (r.ContractId.HasValue)
                contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContractId, token)
                    ?? throw new ValidationFailedException("العقد غير موجود.");

            var note = Mapper.Map<DeliveryNote>(r);
            note.DeliveryNumber = await _numbers.NextAsync("delivery_note", r.Type == "sales_delivery" ? "DN-" : "GRN-", token);
            note.ContractNumber = contract?.ContractNumber ?? r.ContractNumber;
            note.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            note.Status = DeliveryNoteStatus.Delivered;
            note.InvoiceId = null; note.InvoiceNumber = null;
            foreach (var i in note.Items) i.ReturnedQty = 0;
            _db.Add(note);
            await _db.SaveChangesAsync(token);
            return await GetAsync(note.Id, token);
        }, ct);

    public Task<DeliveryNoteDto> UpdateAsync(Guid id, UpdateDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException("بيان التسليم غير موجود");
            if (note.InvoiceId != null || note.Status is not (DeliveryNoteStatus.Delivered or DeliveryNoteStatus.Draft))
                throw new ConflictException("لا يُعدَّل بيان فُوتر أو عليه مرتجعات.");
            if (await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token)) throw new ConflictException("عليه مرتجعات.");

            var errors = new List<string>();
            TradeHelper.RequireParty(r.PartyName, errors);
            if (r.Items.Count == 0) errors.Add("يجب إدخال بند واحد على الأقل.");
            if (r.Items.Any(i => i.DeliveredQty < 0 || i.ContractQty < 0)) errors.Add("الكميات لا تكون سالبة.");
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            var keep = (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber);
            Mapper.Apply(r, note);
            (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber) = keep;
            foreach (var removed in Mapper.SyncCollections(r, note, added => _db.Add(added))) _db.Remove(removed);
            foreach (var i in note.Items) i.ReturnedQty = 0;
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException("بيان التسليم غير موجود");
            if (note.InvoiceId != null) throw new ConflictException("لا يُحذف بيان فُوتر.");
            if (await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token)) throw new ConflictException("احذف مرتجعاته أولاً.");
            _db.RemoveRange(note.Items);
            _db.Remove(note);
            await _db.SaveChangesAsync(token);
        }, ct);

    public async Task<DeliveryReturnNoteDto> GetReturnAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<DeliveryReturnNoteDto>(await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException("مرتجع التسليم غير موجود"));

    public Task DeleteReturnAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var ret = await _db.Set<DeliveryReturnNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException("مرتجع التسليم غير موجود");
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == ret.DeliveryNoteId, token);
            if (note != null)
            {
                if (note.InvoiceId != null) throw new ConflictException("البيان الأصلي فُوتر.");
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
                ?? throw new NotFoundException("بيان التسليم غير موجود");
            if (note.Status == DeliveryNoteStatus.Invoiced) throw new ConflictException("لا يمكن إرجاع بيان تمت فوترته.");
            if (r.Items.Count == 0 || r.Items.Any(i => i.Quantity <= 0)) throw new ValidationFailedException("الكميات المرتجعة يجب أن تكون موجبة.");
            if (r.Type is not ("sales_delivery_return" or "purchase_delivery_return")) throw new ValidationFailedException("النوع غير صالح.");

            foreach (var line in r.Items)
            {
                var src = note.Items.FirstOrDefault(i => i.ItemId == line.ItemId)
                    ?? throw new ValidationFailedException("صنف غير موجود في بيان التسليم.");
                var remaining = src.DeliveredQty - src.ReturnedQty;
                if (line.Quantity > remaining)
                    throw new ValidationFailedException($"كمية المرتجع للصنف {src.ItemName} تتجاوز المتاح ({remaining:0.####}).");
                src.ReturnedQty += line.Quantity;
            }
            note.Status = note.Items.All(i => i.ReturnedQty >= i.DeliveredQty) ? DeliveryNoteStatus.Returned : DeliveryNoteStatus.PartiallyReturned;

            var ret = Mapper.Map<DeliveryReturnNote>(r);
            ret.ReturnNumber = await _numbers.NextAsync("delivery_return", "DR-", token);
            ret.DeliveryNumber = note.DeliveryNumber;
            ret.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            ret.Status = "completed";
            foreach (var i in ret.Items)
                i.ItemName = note.Items.First(x => x.ItemId == i.ItemId).ItemName;
            _db.Add(ret);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<DeliveryReturnNoteDto>(await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(n => n.Items).FirstAsync(n => n.Id == ret.Id, token));
        }, ct);

    public Task<InvoiceDto> CreateMilestoneInvoiceAsync(Guid deliveryNoteId, Guid milestoneId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == deliveryNoteId, token)
                ?? throw new NotFoundException("بيان التسليم غير موجود");
            if (note.InvoiceId != null) throw new ConflictException("تمت فوترة هذا البيان مسبقاً.");
            if (note.Status == DeliveryNoteStatus.Returned) throw new ConflictException("البيان مُرتجع بالكامل.");
            if (note.ContractId == null) throw new ValidationFailedException("البيان غير مرتبط بعقد.");
            if (note.Type != "sales_delivery") throw new ConflictException("الفوترة المرحلية لبيانات التسليم البيعية فقط.");

            var invoice = await _contracts.BillMilestoneAsync(note.ContractId.Value, milestoneId, token);
            note.InvoiceId = invoice.Id; note.InvoiceNumber = invoice.InvoiceNumber; note.Status = DeliveryNoteStatus.Invoiced;
            await _db.SaveChangesAsync(token);
            return invoice;
        }, ct);
}
