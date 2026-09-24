using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosSaleReturnService : IPosSaleReturnService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IInvoiceService _invoices;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;

    public PosSaleReturnService(ErpDbContext db, ICurrentUser user, IInvoiceService invoices, INumberSequenceService numbers, ITransactionRunner tx)
    {
        _db = db; _user = user; _invoices = invoices; _numbers = numbers; _tx = tx;
    }

    public Task<PosSalesReturnDto> CreateAsync(CreatePosReturnRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException("سبب الإرجاع مطلوب.");
            var shift = await _db.Set<PosShift>().FirstOrDefaultAsync(s => s.CashierId == me && s.Status == PosShiftStatus.Open, token)
                ?? throw new ConflictException("افتح وردية أولاً لمعالجة المرتجع.");

            var tx = await _db.Set<PosTransaction>().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == r.OriginalTransactionId, token)
                ?? throw new NotFoundException("المعاملة الأصلية غير موجودة");
            if (tx.Status == PosTransactionStatus.Voided) throw new ConflictException("المعاملة ملغاة.");
            if (tx.InvoiceId == null) throw new ConflictException("لا توجد فاتورة مرتبطة بالمعاملة.");

            var previous = (await _db.Set<PosSalesReturnItem>().AsNoTracking()
                    .Where(i => _db.Set<PosSalesReturn>().Any(x => x.Id == i.ReturnId && x.OriginalTransactionId == tx.Id && x.Status == "completed")).ToListAsync(token))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.ReturnQuantity));

            var wanted = r.Items.Count > 0
                ? r.Items
                : tx.Items.Select(i => new PosReturnLineDto { ItemId = i.ItemId, ReturnQuantity = i.Quantity - previous.GetValueOrDefault(i.ItemId) }).Where(l => l.ReturnQuantity > 0).ToList();
            if (wanted.Count == 0) throw new ConflictException("لا توجد كميات متبقية للإرجاع.");

            foreach (var l in wanted)
            {
                var src = tx.Items.FirstOrDefault(i => i.ItemId == l.ItemId) ?? throw new ValidationFailedException("صنف غير موجود في المعاملة.");
                var remaining = src.Quantity - previous.GetValueOrDefault(l.ItemId);
                if (l.ReturnQuantity <= 0 || l.ReturnQuantity > remaining)
                    throw new ValidationFailedException($"كمية الإرجاع للصنف {src.NameAr} يجب أن تكون بين 0 و{remaining:0.####}.");
            }

            var refundMethod = r.RefundMethod switch
            {
                PosRefundMethod.Cash => PaymentMethod.Cash,
                PosRefundMethod.Mada or PosRefundMethod.Card => PaymentMethod.BankCard,
                _ => PaymentMethod.Credit, // رصيد للعميل
            };
            if (r.RefundMethod == PosRefundMethod.StoreCredit && tx.CustomerId == null)
                throw new ValidationFailedException("الرصيد الدائن يتطلب عميلاً مسجّلاً.");

            var creditNote = await _invoices.CreateReturnAsync(new CreateReturnInvoiceRequestDto
            {
                OriginalInvoiceId = tx.InvoiceId.Value, ReturnReason = r.ReturnReason, RefundPaymentMethod = refundMethod,
                Lines = wanted.Select(l => new ReturnLineDto { ItemId = l.ItemId, Quantity = l.ReturnQuantity }).ToList(),
            }, token);

            var ret = new PosSalesReturn
            {
                ReturnNumber = await _numbers.NextAsync("pos_return", "RET-", token),
                OriginalTransactionId = tx.Id, OriginalInvoiceNumber = tx.InvoiceNumber, OriginalInvoiceId = tx.InvoiceId,
                CustomerId = tx.CustomerId, CustomerName = tx.CustomerName, ShiftId = shift.Id, ReturnReason = r.ReturnReason,
                Subtotal = creditNote.Subtotal, VatAmount = creditNote.VatTotal, GrandTotal = creditNote.GrandTotal,
                RefundMethod = r.RefundMethod, Status = "completed", CreditNoteInvoiceId = creditNote.Id,
            };
            foreach (var l in wanted)
            {
                var src = tx.Items.First(i => i.ItemId == l.ItemId);
                var cnLine = creditNote.Items.First(i => i.ItemId == l.ItemId);
                ret.Items.Add(new PosSalesReturnItem
                {
                    ItemId = l.ItemId, ItemCode = src.ItemCode, ItemName = src.NameAr, SoldQuantity = src.Quantity,
                    PreviouslyReturnedQuantity = previous.GetValueOrDefault(l.ItemId), ReturnQuantity = l.ReturnQuantity,
                    UnitPrice = src.UnitPrice, VatRate = src.VatRate, VatAmount = cnLine.VatAmount,
                    Subtotal = cnLine.TotalBeforeVat, TotalWithVat = cnLine.TotalAfterVat,
                });
            }
            _db.Add(ret);

            shift.TotalReturns += ret.GrandTotal;
            if (r.RefundMethod == PosRefundMethod.Cash) shift.TotalCashRefunds += ret.GrandTotal;

            var fullyReturned = tx.Items.All(i => previous.GetValueOrDefault(i.ItemId) + wanted.Where(w => w.ItemId == i.ItemId).Sum(w => w.ReturnQuantity) >= i.Quantity);
            if (fullyReturned) tx.Status = PosTransactionStatus.Returned;

            if (tx.CustomerId.HasValue)
            {
                var loyalty = await _db.Set<CustomerLoyalty>().FirstOrDefaultAsync(l => l.CustomerId == tx.CustomerId, token);
                if (loyalty != null)
                {
                    var revoke = Math.Min(loyalty.PointsBalance, (int)Math.Floor(ret.GrandTotal / PosPricing.SarPerEarnedPoint));
                    loyalty.PointsBalance -= revoke;
                    ret.PointsRevoked = revoke;
                    loyalty.PointsValueSar = loyalty.PointsBalance * PosPricing.PointValueSar;
                }
            }

            await _db.SaveChangesAsync(token);
            return Mapper.Map<PosSalesReturnDto>(ret);
        }, ct);

    public async Task<PosSalesReturnDto> UpdateAsync(Guid id, UpdatePosReturnRequestDto r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException("سبب الإرجاع مطلوب.");
        var ret = await _db.Set<PosSalesReturn>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("المرتجع غير موجود");
        var shift = await _db.Set<PosShift>().FirstAsync(s => s.Id == ret.ShiftId, ct);
        PosShiftRules.EnsureCanCorrect(shift, _user);
        ret.ReturnReason = r.ReturnReason.Trim();
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<PosSalesReturnDto>(ret);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var ret = await _db.Set<PosSalesReturn>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("المرتجع غير موجود");
            var shift = await _db.Set<PosShift>().FirstAsync(s => s.Id == ret.ShiftId, token);
            PosShiftRules.EnsureCanCorrect(shift, _user);

            shift.TotalReturns -= ret.GrandTotal;
            if (ret.RefundMethod == PosRefundMethod.Cash) shift.TotalCashRefunds -= ret.GrandTotal;
            PosShiftRules.RecomputeVariance(shift);

            var tx = await _db.Set<PosTransaction>().FirstOrDefaultAsync(t => t.Id == ret.OriginalTransactionId, token);
            if (tx != null && tx.Status == PosTransactionStatus.Returned) tx.Status = PosTransactionStatus.Completed;

            if (ret.CustomerId.HasValue && ret.PointsRevoked > 0)
            {
                var loyalty = await _db.Set<CustomerLoyalty>().FirstOrDefaultAsync(l => l.CustomerId == ret.CustomerId, token);
                if (loyalty != null)
                {
                    loyalty.PointsBalance += ret.PointsRevoked;
                    loyalty.PointsValueSar = loyalty.PointsBalance * PosPricing.PointValueSar;
                }
            }

            var creditNoteId = ret.CreditNoteInvoiceId;
            _db.Remove(ret);
            await _db.SaveChangesAsync(token);
            if (creditNoteId.HasValue) await _invoices.DeleteSourceInvoiceAsync(creditNoteId.Value, token);
        }, ct);

    public async Task<PagedResult<PosSalesReturnDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<PosSalesReturn>().AsNoTracking().AsQueryable();
        if (p.StartDate.HasValue) q = q.Where(t => t.CreatedAt >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(t => t.CreatedAt <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(x => x.ReturnNumber.Contains(t) || x.OriginalInvoiceNumber.Contains(t) || x.CustomerName.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(t => t.Items).OrderByDescending(t => t.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<PosSalesReturnDto>
        {
            Items = items.Select(Mapper.Map<PosSalesReturnDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<PosSalesReturnDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<PosSalesReturnDto>(await _db.Set<PosSalesReturn>().AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("المرتجع غير موجود"));
}
